import { useState, useRef, useEffect } from 'react'
import { useParams, Link } from "react-router-dom"

import { Chart } from 'chart.js/auto'
import zoomPlugin from 'chartjs-plugin-zoom'
import { COLORS } from './colors'

Chart.register(zoomPlugin)

const LIVE_HISTORY_MS = 5 * 24 * 3600 * 1000

// The main chart's x window is shifted forward by this much, so "now" sits this far in
// from the right edge and the strip beyond it is forecast-only (nothing measured exists
// there). The window keeps its LIVE_HISTORY_MS width, so this comes out of the history side.
const FUTURE_MS = 24 * 3600 * 1000
// Forecasts are fetched in one batch reaching 2×FUTURE_MS ahead and re-fetched once the
// covered end is within FUTURE_REFRESH_MARGIN_MS of the visible right edge — a spectrum
// is a fixed function of time, so the curve doesn't change as "now" advances, only the
// window sliding over it does.
const FUTURE_REFRESH_MARGIN_MS = 6 * 3600 * 1000
const FUTURE_STEP_MINUTES = 15

// Mirrors the backend's DispatchStatus enum (HomeSysLogic.cs) — one color per reason the
// inverter is doing what it's doing right now, used to color the price line by segment.
const DISPATCH_STATUS_COLORS: Record<string, string> = {
  SellBatteryFull: '#22c55e',
  SellHighPrice: '#86efac',
  SellNoBattery: '#86efac',
  BuyBatteryEmpty: '#ef4444',
  BuyLowPrice: '#fca5a5',
  BuyNoBattery: '#fca5a5',
  ChargeOffgrid: '#3b82f6',
  DrainOffgrid: '#a855f7',
}
const DISPATCH_STATUS_DEFAULT_COLOR = '#f59e0b'
const DISPATCH_STATUS_LEGEND: [string, string][] = [
  ['Sell (battery full)', DISPATCH_STATUS_COLORS.SellBatteryFull],
  ['Sell (price high)', DISPATCH_STATUS_COLORS.SellHighPrice],
  ['Buy (battery empty)', DISPATCH_STATUS_COLORS.BuyBatteryEmpty],
  ['Buy (price low)', DISPATCH_STATUS_COLORS.BuyLowPrice],
  ['Charge (offgrid)', DISPATCH_STATUS_COLORS.ChargeOffgrid],
  ['Drain (offgrid)', DISPATCH_STATUS_COLORS.DrainOffgrid],
]

interface DeviceInfo  {
    id: number
    name: string
    type: string
}

// Spectral prediction only makes sense for devices that follow a repeating natural
// curve — Generators and Consumers. Batteries/EVs follow dispatch decisions (not a
// predictable periodic signal), and the Inverter has no PowerReadings of its own at
// all, so both would just waste an /analyze call and clutter the chart with a
// meaningless "(predicted)" line.
const PREDICTABLE_TYPES = new Set(['SolarPowerPlant', 'WindPowerPlant', 'Consumer'])
const isPredictable = (type: string) => PREDICTABLE_TYPES.has(type)

interface AccumulatorStatus {
  deviceId: number
  name: string
  type: string
  mode: string
  currentChargeKWH: number
  capacityKWH: number
  socPercent: number
  currentKw: number
}

interface HomeSystemStatus {
  timestamp: string
  inverterMode: string
  inverterCurrent: number
  netGridKw: number
  generatedKw: number
  acConsumption: number
  actualGenerationKw: number
  predictedBatteryKw: number | null
  activeAccPriority: number
  accumulators: AccumulatorStatus[]
  scenario: string
  status: string
  overloaded: boolean
  overgenerating: boolean
}

function HomeSystemPage()
{
  // Kept as raw text (not a number) so intermediate states like "0." don't get
  // silently coerced away mid-keystroke — Number("0.") is 0, and a controlled input
  // that immediately re-renders with value=0 wipes out the "." you just typed.
  const [simHoursPerTickText, setSimHoursPerTickText] = useState('1')
  const simHoursPerTick = Number(simHoursPerTickText) || 0

  // Off by default — see RecordingSettings.cs. Turning it on means every tick from now
  // on writes real PowerReading rows for /analyze to later read; leaving it off (the
  // default) means casual speed-changing/testing doesn't keep polluting that history.
  const [recordPowerReadings, setRecordPowerReadings] = useState(false)

  const [backfillDaysText, setBackfillDaysText] = useState('30')
  const backfillDays = Number(backfillDaysText) || 0
  const [backfilling, setBackfilling] = useState(false)

  // Single shared simulated clock, driven by real elapsed time rather than incremented
  // per JS interval tick — this is what lets every chart (power/price, which evaluate a
  // curve at an arbitrary simulated timestamp, and SoC/state, which reflect real backend
  // ticks) agree on "simulated now". The backend advances by hoursPerRealSecond simulated
  // hours per real second (its PeriodicTimer is fixed at 1s), so mirroring that same rate
  // here keeps the frontend's notion of simulated time in lockstep with what the backend
  // is actually doing, independent of how often any given interval happens to fire.
  // Rebased (see the effect below) whenever the rate changes, so a mid-session speed
  // change doesn't cause a discontinuous jump.
  const simClockRef = useRef({ simMs: Date.now(), realMs: Date.now(), hoursPerRealSecond: simHoursPerTick })
  const currentSimTimeMs = () => {
    const { simMs, realMs, hoursPerRealSecond } = simClockRef.current
    return simMs + (Date.now() - realMs) * hoursPerRealSecond * 3600
  }
  const simTimeRef = useRef<Date>(new Date())

  const [visible, setVisible] = useState<Record<string, boolean>>({})

  const chartCanvasRef = useRef<HTMLCanvasElement>(null)
  const chartRef = useRef<Chart | null>(null)
  const dataByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  // Actual grid exchange for the main chart, sourced from status.inverterCurrent
  // (positive=selling there) and sign-flipped when pushed, so this line goes UP when
  // we buy and DOWN when we sell. Separate from gridPowerDataRef, which keeps the
  // inverter's own sign for the state chart.
  const gridDataRef = useRef<{ x: number; y: number }[]>([])
  const predictedDataByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  // Sourced from status.predictedBatteryKw (computed backend-side, in HomeSysLogic) —
  // one aggregate line, not per-battery; see PredictedBatteryKw's own comment for the
  // sign convention and formula.
  const batteryPredictedDataRef = useRef<{ x: number; y: number }[]>([])
  // Forecast beyond "now" for the shifted chart window (see FUTURE_MS): per-device from
  // /devices/{id}/predict, battery from /home-systems/{id}/predict-battery — so the
  // battery formula stays defined once, in HomeSysLogic. futureCoveredUntilRef is the
  // sim-time (ms) the last batch reaches; 0 forces a re-fetch on the next tick.
  const futurePredictedByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  const futureBatteryPredictedRef = useRef<{ x: number; y: number }[]>([])
  const futureCoveredUntilRef = useRef(0)
  // Whether /predict actually gets fetched each tick — a ref (not just the mirrored
  // predictShown state below) because tick()'s closure reads it without re-running the
  // effect on every toggle.
  const predictEnabledRef = useRef(false)
  const [predictShown, setPredictShown] = useState(false)
  const [log, setLog] = useState('')
  const addLog = (msg: string) => setLog((prev) => prev + msg + '\n')



  const { id } = useParams<{id: string}>()
  const homeSystemId = Number(id)

  const [devices, setDevices] = useState<DeviceInfo []>([])
  const [powers, setPowers] = useState<Record<number, number>>({})

  // --- Price chart ---
  const [gridNodeId, setGridNodeId] = useState(0)
  const priceChartCanvasRef = useRef<HTMLCanvasElement>(null)
  const priceChartRef = useRef<Chart | null>(null)
  const priceDataRef = useRef<{ x: number; y: number; status: string }[]>([])
  // Updated by the (slower, 1s) status poll; read by the (faster, 200ms) price tick so
  // each price point can be tagged with whatever dispatch status was current at the time —
  // the price curve itself has no idea about battery/inverter state, so the two have to be
  // joined this way rather than coming from the same request.
  const latestDispatchStatusRef = useRef('')

  // --- Battery SoC + live-state charts (real wall-clock time, client-side only) ---
  const [socVisible, setSocVisible] = useState<Record<number, boolean>>({})
  const [stateVisible, setStateVisible] = useState<Record<string, boolean>>({ grid: true })
  const [accNames, setAccNames] = useState<Record<number, string>>({})
  const [inverterMode, setInverterMode] = useState('')
  const [netGridKw, setNetGridKw] = useState(0)
  const [generatedKw, setGeneratedKw] = useState(0)
  const [acConsumption, setAcConsumption] = useState(0)
  const [actualGenerationKw, setActualGenerationKw] = useState(0)
  const [inverterCurrent, setInverterCurrent] = useState(0)
  // "Active" battery: the one HomeSysLogic actually put into Charging/Draining this
  // tick, inferred from Mode — the most reliable externally-observable signal of which
  // accumulator the backend is currently using.
  const [activeAccumulator, setActiveAccumulator] = useState<AccumulatorStatus | null>(null)
  const [scenario, setScenario] = useState('')
  const [overloaded, setOverloaded] = useState(false)
  const [overgenerating, setOvergenerating] = useState(false)
  const [allAccumulators, setAllAccumulators] = useState<AccumulatorStatus[]>([])
  const [activeAccPriority, setActiveAccPriority] = useState(0)

  const socChartCanvasRef = useRef<HTMLCanvasElement>(null)
  const socChartRef = useRef<Chart | null>(null)
  const socDataByAcc = useRef<Map<number, { x: number; y: number }[]>>(new Map())

  const stateChartCanvasRef = useRef<HTMLCanvasElement>(null)
  const stateChartRef = useRef<Chart | null>(null)
  const stateDataByAcc = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  const gridPowerDataRef = useRef<{ x: number; y: number }[]>([])



  useEffect(() => {
    fetch(`/home-systems/${homeSystemId}/devices`)
      .then((res) => (res.ok ? res.json() : []))
      .then((list: DeviceInfo[]) => {
        setDevices(list)
        const initial: Record<string, boolean> = { grid: true, batteryPredicted: true }
        for (const d of list) initial[d.id] = true
        setVisible(initial)
      })
  }, [homeSystemId])

  useEffect(() => {
    fetch(`/home-systems/${homeSystemId}`)
      .then((res) => (res.ok ? res.json() : null))
      .then((hs) => { if (hs) setGridNodeId(hs.gridNodeId) })
  }, [homeSystemId])

  // RecordingSettings is a backend-wide singleton (not per-home-system), so this
  // reflects whatever any client last set it to — fetched once on mount rather than
  // assumed, in case it was already turned on from another tab/session.
  useEffect(() => {
    fetch('/simulation/recording')
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => { if (data) setRecordPowerReadings(data.recordPowerReadings) })
  }, [])

  function handleToggleRecording(checked: boolean) {
    setRecordPowerReadings(checked)
    fetch('/simulation/recording', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ recordPowerReadings: checked }),
    })
  }

  // The same "Sim hours/tick" control also drives the real backend simulation speed
  // (HomeSysLogic applies this many simulated hours per real tick), so the SoC and
  // live-state charts speed up together with the curve-based power/price charts. Rebase
  // the shared clock first, using the OLD rate still sitting in the ref, so the rate
  // change takes effect smoothly from "now" instead of jumping.
  useEffect(() => {
    simClockRef.current = { simMs: currentSimTimeMs(), realMs: Date.now(), hoursPerRealSecond: simHoursPerTick }

    fetch('/simulation/speed', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ hoursPerTick: simHoursPerTick }),
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [simHoursPerTick])


  useEffect(() => {
    if (devices.length === 0) return
    if (chartRef.current) chartRef.current.destroy()
    dataByDevice.current.clear()
    gridDataRef.current = []
    predictedDataByDevice.current.clear()
    batteryPredictedDataRef.current = []
    futurePredictedByDevice.current.clear()
    futureBatteryPredictedRef.current = []
    futureCoveredUntilRef.current = 0
    predictEnabledRef.current = false
    simClockRef.current = { simMs: Date.now(), realMs: Date.now(), hoursPerRealSecond: simHoursPerTick }
    simTimeRef.current = new Date(currentSimTimeMs())

    chartRef.current = new Chart(chartCanvasRef.current!, {
      type: 'line',
      data: { datasets: [] },
      options: {
        parsing: false,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: {
            type: 'linear',
            min: simTimeRef.current.getTime() + FUTURE_MS - LIVE_HISTORY_MS,
            max: simTimeRef.current.getTime() + FUTURE_MS,
            ticks: { callback: (v) => new Date(v as number).toLocaleDateString() },
          },
          y: { min: -8, max: 8, title: { display: true, text: 'kW' } },
        },
        plugins: {
          // The checkboxes above already toggle each dataset's visibility — with 5
          // devices × actual+predicted plus the aggregate lines, the auto-generated legend was
          // eating most of the chart's height, squeezing the actual plot into a sliver.
          legend: { display: false },
          zoom: {
            pan: { enabled: true, mode: 'x' },
            zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' },
          },
        },
      },
    })
  }, [devices])


  useEffect(() => {
    if (!gridNodeId) return
    if (priceChartRef.current) priceChartRef.current.destroy()
    priceDataRef.current = []

    priceChartRef.current = new Chart(priceChartCanvasRef.current!, {
      type: 'line',
      data: { datasets: [] },
      options: {
        parsing: false,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: {
            type: 'linear',
            min: simTimeRef.current.getTime() - LIVE_HISTORY_MS,
            max: simTimeRef.current.getTime(),
            ticks: { callback: (v) => new Date(v as number).toLocaleDateString() },
          },
          y: { title: { display: true, text: 'Price' } },
        },
        plugins: {
          zoom: {
            pan: { enabled: true, mode: 'x' },
            zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' },
          },
        },
      },
    })
  }, [gridNodeId])


  useEffect(() => {
    if (devices.length === 0) return

    const toPoints = (arr: { timestamp: string; predictedKw: number }[]) =>
      arr.map((p) => ({ x: new Date(p.timestamp).getTime(), y: p.predictedKw }))

    // Fetches one forecast batch from `nowMs` to 2×FUTURE_MS ahead for every predictable
    // device plus the battery. A device/battery that isn't analyzed yet 404s and just
    // contributes nothing (analyzing resets futureCoveredUntilRef, so this retries then).
    const refreshFuturePredictions = async (nowMs: number) => {
      const query = `from=${new Date(nowMs).toISOString()}&to=${new Date(nowMs + 2 * FUTURE_MS).toISOString()}&stepMinutes=${FUTURE_STEP_MINUTES}`
      const getPoints = (url: string) =>
        fetch(url).then((r) => (r.ok ? r.json() : null)).then((arr) => (arr ? toPoints(arr) : null))
      const [perDevice, battery] = await Promise.all([
        Promise.all(
          devices
            .filter((d) => isPredictable(d.type))
            .map(async (d) => [d.id, await getPoints(`/devices/${d.id}/predict?${query}`)] as const)
        ),
        getPoints(`/home-systems/${homeSystemId}/predict-battery?${query}`),
      ])
      // Predictions may have been switched off while this was in flight — don't
      // resurrect data the toggle just cleared.
      if (!predictEnabledRef.current) return
      for (const [deviceId, points] of perDevice) {
        if (points) futurePredictedByDevice.current.set(deviceId, points)
      }
      futureBatteryPredictedRef.current = battery ?? []
      futureCoveredUntilRef.current = nowMs + 2 * FUTURE_MS
    }

    // A predicted line's measured-era points followed by its forecast — the forecast
    // batch starts at whatever "now" was when it was fetched, so anything at or before
    // the last live-predicted point is dropped to keep x strictly increasing.
    const withFuture = (past: { x: number; y: number }[], future: { x: number; y: number }[]) => {
      const lastX = past.length ? past[past.length - 1].x : -Infinity
      return [...past, ...future.filter((p) => p.x > lastX)]
    }

    const tick = async () => {

      // No `at` is sent for /live — each call gets back the backend's own
      // authoritative SimulationClock reading instead of a client-guessed one. A
      // guessed `at` that fell behind the accumulator's own last-committed ramp tick
      // made RampedCurrentAt return a frozen value instead of a live one, which is
      // what caused the battery line to staircase instead of ramping smoothly.
      const [results, priceRes] = await Promise.all([
        Promise.all(
          devices.map(async (d) => {
            const liveRes = await fetch(`/devices/${d.id}/live`)
            const { timestamp, powerKw } = liveRes.ok
              ? await liveRes.json()
              : { timestamp: new Date().toISOString(), powerKw: 0 }
            // Predict is asked about the exact instant live just reported (not a
            // separately-guessed one), so the two curves stay directly comparable.
            // Only Generator/Consumer devices ever get analyzed, so only fetch a
            // prediction for those — a Battery/Inverter id would just 404.
            const predicted = predictEnabledRef.current && isPredictable(d.type)
              ? await fetch(`/devices/${d.id}/predict?from=${timestamp}&to=${timestamp}`)
                  .then((r) => (r.ok ? r.json() : null))
                  .then((arr) => arr?.[0] ?? null)
              : null
            return { id: d.id, timestamp, powerKw, predicted }
          })
        ),
        gridNodeId ? fetch(`/grid/${gridNodeId}/live`) : Promise.resolve(null),
      ])

      simTimeRef.current = new Date(results[0]?.timestamp ?? currentSimTimeMs())

      const nextPowers: Record<number, number> = {}
      const cutoff = simTimeRef.current.getTime() - LIVE_HISTORY_MS
      for (const r of results) {
        nextPowers[r.id] = r.powerKw
        const arr = dataByDevice.current.get(r.id) ?? []
        arr.push({ x: new Date(r.timestamp).getTime(), y: r.powerKw })
        while (arr.length && arr[0].x < cutoff) arr.shift()
        dataByDevice.current.set(r.id, arr)

        if (r.predicted) {
          const parr = predictedDataByDevice.current.get(r.id) ?? []
          parr.push({ x: new Date(r.predicted.timestamp).getTime(), y: r.predicted.predictedKw })
          while (parr.length && parr[0].x < cutoff) parr.shift()
          predictedDataByDevice.current.set(r.id, parr)
        }
      }
      setPowers(nextPowers)

      if (
        predictEnabledRef.current &&
        futureCoveredUntilRef.current < simTimeRef.current.getTime() + FUTURE_MS + FUTURE_REFRESH_MARGIN_MS
      ) {
        await refreshFuturePredictions(simTimeRef.current.getTime())
      }

      if (priceRes && priceRes.ok) {
        const { timestamp, powerKw: price } = await priceRes.json()
        priceDataRef.current.push({ x: new Date(timestamp).getTime(), y: price, status: latestDispatchStatusRef.current })
        while (priceDataRef.current.length && priceDataRef.current[0].x < cutoff) priceDataRef.current.shift()
      }

      if (chartRef.current) {
        chartRef.current.data.datasets = [
          ...devices.map((d, i) => ({
            label: `${d.name} (${d.type})`,
            data: dataByDevice.current.get(d.id) ?? [],
            borderColor: COLORS[i % COLORS.length],
            borderWidth: 1,
            pointRadius: 0,
            hidden: !visible[d.id],
          })),
          ...devices
            .filter((d) => isPredictable(d.type))
            .map((d) => ({
              label: `${d.name} (predicted)`,
              data: withFuture(predictedDataByDevice.current.get(d.id) ?? [], futurePredictedByDevice.current.get(d.id) ?? []),
              // Same color as this device's actual line, found by its position in the
              // full (unfiltered) device list, so the two stay visually paired.
              borderColor: COLORS[devices.findIndex((dd) => dd.id === d.id) % COLORS.length],
              borderDash: [4, 4],
              borderWidth: 1,
              pointRadius: 0,
              hidden: !visible[d.id],
            })),
          {
            label: 'Grid (+buying / -selling)',
            data: gridDataRef.current,
            borderColor: '#FF0000',
            borderWidth: 2,
            pointRadius: 0,
            hidden: !visible['grid'],
          },
          {
            // Predicted net battery power, computed backend-side in HomeSysLogic. One
            // aggregate line (not per-battery).
            label: 'Battery (predicted)',
            data: withFuture(batteryPredictedDataRef.current, futureBatteryPredictedRef.current),
            borderColor: '#a855f7',
            borderDash: [4, 4],
            borderWidth: 2,
            pointRadius: 0,
            hidden: !visible['batteryPredicted'],
          },
        ]
        chartRef.current.options.scales!.x!.min = simTimeRef.current.getTime() + FUTURE_MS - LIVE_HISTORY_MS
        chartRef.current.options.scales!.x!.max = simTimeRef.current.getTime() + FUTURE_MS
        chartRef.current.update()
      }

      if (priceChartRef.current) {
        priceChartRef.current.data.datasets = [
          {
            label: 'Price',
            data: priceDataRef.current,
            borderColor: '#f59e0b',
            borderWidth: 2,
            pointRadius: 0,
            // Colors each segment by the dispatch status in effect at its END point —
            // one dataset, but the line changes color wherever the reason for buying/
            // selling/charging/draining changed, instead of a single flat color.
            segment: {
              borderColor: (ctx: any) =>
                DISPATCH_STATUS_COLORS[ctx.p1.raw.status as string] ?? DISPATCH_STATUS_DEFAULT_COLOR,
            },
          },
        ]
        priceChartRef.current.options.scales!.x!.min = simTimeRef.current.getTime() - LIVE_HISTORY_MS
        priceChartRef.current.options.scales!.x!.max = simTimeRef.current.getTime()
        priceChartRef.current.update()
      }
    }

    // Self-scheduling instead of setInterval: a single tick() awaits several /live
    // requests in parallel, and each one alone measures ~130ms round-trip (chained EF
    // queries per device). That's uncomfortably close to the 200ms period — setInterval
    // would fire the next tick before a slow one finishes, letting two overlap and
    // resolve out of order, so whichever happened to win the race got plotted regardless
    // of which was actually newer. Waiting for one tick to fully finish before scheduling
    // the next makes that impossible, at the cost of the true interval stretching a bit
    // past 200ms whenever a request is slow — still strictly better than a stale/wrong
    // point winning a race.
    let cancelled = false
    let timeoutId: ReturnType<typeof setTimeout>
    const scheduleNext = () => { if (!cancelled) timeoutId = setTimeout(runTick, 200) }
    const runTick = () => { tick().finally(scheduleNext) }
    runTick()
    return () => { cancelled = true; clearTimeout(timeoutId) }
    // simHoursPerTick isn't a dep here — the shared clock ref already reflects the
    // current rate, so a speed change takes effect on this interval's very next fire
    // without needing to tear it down and rebuild it.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [devices, visible, gridNodeId])


  // Built once per homeSystemId — separate from the poll loop below so toggling
  // visibility doesn't tear down and lose the accumulated real-time history.
  useEffect(() => {
    if (!homeSystemId) return
    if (socChartRef.current) socChartRef.current.destroy()
    if (stateChartRef.current) stateChartRef.current.destroy()
    socDataByAcc.current.clear()
    stateDataByAcc.current.clear()
    gridPowerDataRef.current = []

    const zoomOpts = {
      pan: { enabled: true, mode: 'x' as const },
      zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' as const },
    }

    socChartRef.current = new Chart(socChartCanvasRef.current!, {
      type: 'line',
      data: { datasets: [] },
      options: {
        parsing: false,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: { type: 'linear', ticks: { callback: (v) => new Date(v as number).toLocaleDateString() } },
          y: { min: 0, max: 100, title: { display: true, text: 'SoC %' } },
        },
        plugins: { zoom: zoomOpts },
      },
    })

    stateChartRef.current = new Chart(stateChartCanvasRef.current!, {
      type: 'line',
      data: { datasets: [] },
      options: {
        parsing: false,
        maintainAspectRatio: false,
        animation: false,
        scales: {
          x: { type: 'linear', ticks: { callback: (v) => new Date(v as number).toLocaleDateString() } },
          y: { title: { display: true, text: 'kW (+selling/discharging, -buying/charging)' } },
        },
        plugins: { zoom: zoomOpts },
      },
    })
  }, [homeSystemId])


  useEffect(() => {
    if (!homeSystemId) return

    const poll = async () => {
      const res = await fetch(`/home-systems/${homeSystemId}/status`)
      if (!res.ok) return
      const status: HomeSystemStatus = await res.json()
      // Use the backend's own authoritative simulated timestamp for this snapshot,
      // not the frontend's independently-drifting clock estimate (currentSimTimeMs()) —
      // that guess and the backend's real SimulationClock never resync after they start,
      // so plotting on the guess put correct values at a slightly wrong horizontal spot.
      const now = new Date(status.timestamp).getTime()
      const cutoff = now - LIVE_HISTORY_MS

      latestDispatchStatusRef.current = status.status
      setInverterMode(status.inverterMode)
      setNetGridKw(status.netGridKw)
      setGeneratedKw(status.generatedKw)
      setAcConsumption(status.acConsumption)
      setActualGenerationKw(status.actualGenerationKw)
      setAllAccumulators(status.accumulators)
      setActiveAccPriority(status.activeAccPriority)
      setInverterCurrent(status.inverterCurrent)
      setActiveAccumulator(status.accumulators.find((a) => a.mode === 'Charging' || a.mode === 'Draining') ?? null)
      setScenario(status.scenario)
      setOverloaded(status.overloaded)
      setOvergenerating(status.overgenerating)

      gridPowerDataRef.current.push({ x: now, y: status.inverterCurrent })
      while (gridPowerDataRef.current.length && gridPowerDataRef.current[0].x < cutoff) gridPowerDataRef.current.shift()

      // Main-chart grid line: inverterCurrent is +selling/-buying, and this line is
      // wanted the other way round (up = buying, down = selling), hence the negation.
      gridDataRef.current.push({ x: now, y: -status.inverterCurrent })
      while (gridDataRef.current.length && gridDataRef.current[0].x < cutoff) gridDataRef.current.shift()

      // Computed backend-side now (HomeSysLogic) — null until every Generator/Consumer
      // has been analyzed at least once.
      if (status.predictedBatteryKw !== null) {
        batteryPredictedDataRef.current.push({ x: now, y: status.predictedBatteryKw })
        while (batteryPredictedDataRef.current.length && batteryPredictedDataRef.current[0].x < cutoff) batteryPredictedDataRef.current.shift()
      }

      let namesChanged = false
      const nextNames = { ...accNames }
      for (const acc of status.accumulators) {
        if (!(acc.deviceId in nextNames)) { nextNames[acc.deviceId] = acc.name; namesChanged = true }

        const socArr = socDataByAcc.current.get(acc.deviceId) ?? []
        socArr.push({ x: now, y: acc.socPercent })
        while (socArr.length && socArr[0].x < cutoff) socArr.shift()
        socDataByAcc.current.set(acc.deviceId, socArr)

        const stateArr = stateDataByAcc.current.get(acc.deviceId) ?? []
        stateArr.push({ x: now, y: acc.currentKw })
        while (stateArr.length && stateArr[0].x < cutoff) stateArr.shift()
        stateDataByAcc.current.set(acc.deviceId, stateArr)
      }
      if (namesChanged) setAccNames(nextNames)

      if (socChartRef.current) {
        socChartRef.current.data.datasets = status.accumulators.map((acc, i) => ({
          label: acc.name,
          data: socDataByAcc.current.get(acc.deviceId) ?? [],
          borderColor: COLORS[i % COLORS.length],
          borderWidth: 1,
          pointRadius: 0,
          hidden: !(socVisible[acc.deviceId] ?? true),
        }))
        socChartRef.current.options.scales!.x!.min = cutoff
        socChartRef.current.options.scales!.x!.max = now
        socChartRef.current.update()
      }

      if (stateChartRef.current) {
        stateChartRef.current.data.datasets = [
          {
            label: `Grid (${status.inverterMode})`,
            data: gridPowerDataRef.current,
            borderColor: '#FF0000',
            borderWidth: 2,
            pointRadius: 0,
            hidden: !(stateVisible['grid'] ?? true),
          },
          ...status.accumulators.map((acc, i) => ({
            label: `${acc.name} (${acc.mode})`,
            data: stateDataByAcc.current.get(acc.deviceId) ?? [],
            borderColor: COLORS[i % COLORS.length],
            borderWidth: 1,
            pointRadius: 0,
            hidden: !(stateVisible[acc.deviceId] ?? true),
          })),
        ]
        stateChartRef.current.options.scales!.x!.min = cutoff
        stateChartRef.current.options.scales!.x!.max = now
        stateChartRef.current.update()
      }
    }

    // Self-scheduling for the same reason as the power chart's tick() — /status runs a
    // multi-table join and can take a while; waiting for one poll to finish before
    // scheduling the next avoids overlapping requests resolving out of order.
    let cancelled = false
    let timeoutId: ReturnType<typeof setTimeout>
    const scheduleNext = () => { if (!cancelled) timeoutId = setTimeout(runPoll, 1000) }
    const runPoll = () => { poll().finally(scheduleNext) }
    runPoll()
    return () => { cancelled = true; clearTimeout(timeoutId) }
  }, [homeSystemId, socVisible, stateVisible, accNames])


  // Keeps the ref tick() actually reads in sync with the checkbox shown in the UI —
  // used both by that checkbox directly and by handleAnalyze/handleBackfillAndAnalyze,
  // which turn prediction on as a side effect of computing something to predict from.
  function setPredictionShown(shown: boolean) {
    predictEnabledRef.current = shown
    setPredictShown(shown)
    // Turning on (including via a fresh analysis, whose new spectrum invalidates any old
    // forecast) forces the next tick to fetch the forecast; turning off drops it.
    futureCoveredUntilRef.current = 0
    if (!shown) {
      futurePredictedByDevice.current.clear()
      futureBatteryPredictedRef.current = []
    }
  }

  async function handleAnalyze() {
    for (const d of devices.filter((d) => isPredictable(d.type))) {
      addLog(`Analyzing device ${d.id} (${d.name})...`)
      const res = await fetch(`/devices/${d.id}/analyze`, { method: 'POST' })
      if (!res.ok) { addLog(`Analyze failed for device ${d.id}: ${res.status} ${await res.text()}`); continue }
    }
    setPredictionShown(true)
    addLog('Prediction enabled — now plotting alongside live data.')
  }

  // Generates a clean, perfectly-evenly-spaced (hourly) history via /readings/generate
  // instead of relying on live-recorded ticks — even with Fourier.cs's hoursPerSample
  // fix, live ticks are still spaced irregularly (real tick jitter, sim-speed changes),
  // where a backfill gives the FFT an ideal uniform grid to work with.
  async function handleBackfillAndAnalyze() {
    setBackfilling(true)
    for (const d of devices.filter((d) => isPredictable(d.type))) {
      addLog(`Backfilling ${backfillDays} days for device ${d.id} (${d.name})...`)
      const genRes = await fetch(`/devices/${d.id}/readings/generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ days: backfillDays }),
      })
      if (!genRes.ok) { addLog(`Backfill failed for device ${d.id}: ${genRes.status} ${await genRes.text()}`); continue }

      addLog(`Analyzing device ${d.id} (${d.name})...`)
      const analyzeRes = await fetch(`/devices/${d.id}/analyze`, { method: 'POST' })
      if (!analyzeRes.ok) { addLog(`Analyze failed for device ${d.id}: ${analyzeRes.status} ${await analyzeRes.text()}`); continue }
    }
    setPredictionShown(true)
    setBackfilling(false)
    addLog('Backfill + analysis complete — prediction enabled.')
  }

  return (
    <div>
      <label>
        Sim hours/tick{' '}
        <input type="number" step="0.01" value={simHoursPerTickText} onChange={(e) => setSimHoursPerTickText(e.target.value)} />
      </label>
      {' '}
      <label title="Off by default — casual speed-changing/testing won't pollute PowerReading history used by Analyze.">
        <input
          type="checkbox"
          checked={recordPowerReadings}
          onChange={(e) => handleToggleRecording(e.target.checked)}
        />
        {' '}Record power readings
      </label>
      <button onClick={handleAnalyze}>Analyze (Live)</button>
      {' '}
      <label>
        Backfill days{' '}
        <input type="number" step="1" min="1" value={backfillDaysText} onChange={(e) => setBackfillDaysText(e.target.value)} style={{ width: '60px' }} />
      </label>
      {' '}
      <button onClick={handleBackfillAndAnalyze} disabled={backfilling}>
        {backfilling ? 'Backfilling…' : 'Backfill & Analyze'}
      </button>
      {' '}
      <label title="Shows generation/consumption predictions (and the derived battery predicted line) without re-running analysis. Only has something to show once a spectrum exists — via Analyze (Live) or Backfill & Analyze.">
        <input
          type="checkbox"
          checked={predictShown}
          onChange={(e) => setPredictionShown(e.target.checked)}
        />
        {' '}Show predictions
      </label>
      {' '}<Link to={`/homesystem/${homeSystemId}/god`}>God mode</Link>
      <h2>Home System {homeSystemId}</h2>

      <div style={{ display: 'flex', gap: '1.5rem', alignItems: 'flex-start' }}>
        {/* Left column: the main power chart */}
        <div style={{ flex: 1, minWidth: 0 }}>
          <ul>
            {devices.map((d) => (
              <li key={d.id}>{d.name} ({d.type}): {(powers[d.id] ?? 0).toFixed(2)} kW</li>
            ))}
          </ul>

          <div>
            {devices.map((d) => (
              <label key={d.id} style={{ marginRight: '1rem' }}>
                <input
                  type="checkbox"
                  checked={visible[d.id] ?? true}
                  onChange={(e) => setVisible((v) => ({ ...v, [d.id]: e.target.checked }))}
                />
                {' '}{d.name}
              </label>
            ))}
            <label>
              <input
                type="checkbox"
                checked={visible['grid'] ?? true}
                onChange={(e) => setVisible((v) => ({ ...v, grid: e.target.checked }))}
              />
              {' '}Grid
            </label>
            <label>
              <input
                type="checkbox"
                checked={visible['batteryPredicted'] ?? true}
                onChange={(e) => setVisible((v) => ({ ...v, batteryPredicted: e.target.checked }))}
              />
              {' '}Battery (predicted)
            </label>
          </div>
          <div style={{ height: '500px' }}>
            <canvas ref={chartCanvasRef}></canvas>
          </div>
        </div>

        {/* Right column: everything else */}
        <div style={{ flex: 1, minWidth: 0 }}>
          <h3>Power Price</h3>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem', marginBottom: '0.5rem' }}>
            {DISPATCH_STATUS_LEGEND.map(([label, color]) => (
              <span key={label} style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', fontSize: '0.85em' }}>
                <span style={{ width: '10px', height: '10px', borderRadius: '2px', background: color, display: 'inline-block' }} />
                {label}
              </span>
            ))}
          </div>
          <div style={{ height: '220px' }}>
            <canvas ref={priceChartCanvasRef}></canvas>
          </div>

          <h3>Battery State of Charge</h3>
          <div>
            {Object.entries(accNames).map(([idStr, name]) => (
              <label key={idStr} style={{ marginRight: '1rem' }}>
                <input
                  type="checkbox"
                  checked={socVisible[Number(idStr)] ?? true}
                  onChange={(e) => setSocVisible((v) => ({ ...v, [Number(idStr)]: e.target.checked }))}
                />
                {' '}{name}
              </label>
            ))}
          </div>
          <div style={{ height: '220px' }}>
            <canvas ref={socChartCanvasRef}></canvas>
          </div>

          <h3>Live State</h3>
          {(overloaded || overgenerating) && (
            <p style={{ color: '#ef4444', fontWeight: 600 }}>
              {overloaded && 'Overload: AC demand exceeds the inverter\'s rated output — excess is being curtailed. '}
              {overgenerating && 'Overgeneration: DC input exceeds the inverter\'s max — excess is being curtailed.'}
            </p>
          )}
          <p>Scenario: <strong>{scenario || '—'}</strong></p>
          <p>Net grid power: {netGridKw.toFixed(2)} kW ({inverterMode || '—'})</p>
          <p>Pure Gen: {generatedKw.toFixed(2)} kW | actualGeneration: {actualGenerationKw.toFixed(2)} kW | Consumption: {acConsumption.toFixed(2)} kW</p>
          <p>Inverter: {inverterMode || '—'}, {inverterCurrent.toFixed(2)} kW</p>
          <p>
            Active battery (priority {activeAccPriority}):{' '}
            {activeAccumulator
              ? `${activeAccumulator.name} (${activeAccumulator.mode}, ${activeAccumulator.currentKw.toFixed(2)} kW, ${activeAccumulator.socPercent.toFixed(0)}% SoC)`
              : 'None (all idle)'}
          </p>
          <p>Battery modes:</p>
          <ul>
            {allAccumulators.map((acc) => (
              <li key={acc.deviceId}>
                {acc.name}: <strong>{acc.mode}</strong> ({acc.currentKw.toFixed(2)} kW, {acc.socPercent.toFixed(0)}% SoC)
              </li>
            ))}
          </ul>
          <div>
            <label style={{ marginRight: '1rem' }}>
              <input
                type="checkbox"
                checked={stateVisible['grid'] ?? true}
                onChange={(e) => setStateVisible((v) => ({ ...v, grid: e.target.checked }))}
              />
              {' '}Grid
            </label>
            {Object.entries(accNames).map(([idStr, name]) => (
              <label key={idStr} style={{ marginRight: '1rem' }}>
                <input
                  type="checkbox"
                  checked={stateVisible[Number(idStr)] ?? true}
                  onChange={(e) => setStateVisible((v) => ({ ...v, [Number(idStr)]: e.target.checked }))}
                />
                {' '}{name}
              </label>
            ))}
          </div>
          <div style={{ height: '220px' }}>
            <canvas ref={stateChartCanvasRef}></canvas>
          </div>

          <pre>{log}</pre>
        </div>
      </div>
    </div>
  )
}

export default HomeSystemPage
