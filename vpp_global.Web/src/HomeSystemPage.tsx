import { useState, useRef, useEffect } from 'react'
import { useParams } from "react-router-dom"

import { Chart } from 'chart.js/auto'
import zoomPlugin from 'chartjs-plugin-zoom'

Chart.register(zoomPlugin)

const LIVE_HISTORY_MS = 5 * 24 * 3600 * 1000

const COLORS = ['#16a34a', '#2563eb', '#f59e0b', '#8b5cf6', '#eb4497']

interface DeviceInfo  {
    id: number
    name: string
    type: string
}

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
  inverterMode: string
  inverterCurrent: number
  netGridKw: number
  generatedKw: number
  acConsumption: number
  generatedAcKw: number
  activeAccPriority: number
  accumulators: AccumulatorStatus[]
  scenario: string
}

function HomeSystemPage()
{
  // Kept as raw text (not a number) so intermediate states like "0." don't get
  // silently coerced away mid-keystroke — Number("0.") is 0, and a controlled input
  // that immediately re-renders with value=0 wipes out the "." you just typed.
  const [simHoursPerTickText, setSimHoursPerTickText] = useState('1')
  const simHoursPerTick = Number(simHoursPerTickText) || 0

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
  const totalDataRef = useRef<{ x: number; y: number }[]>([])
  const predictedDataByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  const totalPredictedRef = useRef<{ x: number; y: number }[]>([])
  const predictEnabledRef = useRef(false)
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
  const priceDataRef = useRef<{ x: number; y: number }[]>([])

  // --- Battery SoC + live-state charts (real wall-clock time, client-side only) ---
  const [socVisible, setSocVisible] = useState<Record<number, boolean>>({})
  const [stateVisible, setStateVisible] = useState<Record<string, boolean>>({ grid: true })
  const [accNames, setAccNames] = useState<Record<number, string>>({})
  const [inverterMode, setInverterMode] = useState('')
  const [netGridKw, setNetGridKw] = useState(0)
  const [generatedKw, setGeneratedKw] = useState(0)
  const [acConsumption, setAcConsumption] = useState(0)
  const [generatedAcKw, setGeneratedAcKw] = useState(0)
  const [inverterCurrent, setInverterCurrent] = useState(0)
  // "Active" battery: the one HomeSysLogic actually put into Charging/Draining this
  // tick, inferred from Mode — the most reliable externally-observable signal of which
  // accumulator the backend is currently using.
  const [activeAccumulator, setActiveAccumulator] = useState<AccumulatorStatus | null>(null)
  const [scenario, setScenario] = useState('')
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
        const initial: Record<string, boolean> = { total: true }
        for (const d of list) initial[d.id] = true
        setVisible(initial)
      })
  }, [homeSystemId])

  useEffect(() => {
    fetch(`/home-systems/${homeSystemId}`)
      .then((res) => (res.ok ? res.json() : null))
      .then((hs) => { if (hs) setGridNodeId(hs.gridNodeId) })
  }, [homeSystemId])

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
    totalDataRef.current = []
    predictedDataByDevice.current.clear()
    totalPredictedRef.current = []
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
            min: simTimeRef.current.getTime() - LIVE_HISTORY_MS,
            max: simTimeRef.current.getTime(),
            ticks: { callback: (v) => new Date(v as number).toLocaleDateString() },
          },
          y: { min: -8, max: 8, title: { display: true, text: 'kW' } },
        },
        plugins: {
          // The checkboxes above already toggle each dataset's visibility — with 5
          // devices × actual+predicted plus Total × 2, the auto-generated legend was
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

    const tick = async () => {

      simTimeRef.current = new Date(currentSimTimeMs())
      const at = simTimeRef.current.toISOString()

      const [results, priceRes] = await Promise.all([
        Promise.all(
          devices.map(async (d) => {
            const [liveRes, predictRes] = await Promise.all([
              fetch(`/devices/${d.id}/live?at=${at}`),   // ← added ?at=
              predictEnabledRef.current ? fetch(`/devices/${d.id}/predict?from=${at}&to=${at}`) : Promise.resolve(null),
            ])
            const { timestamp, powerKw } = liveRes.ok ? await liveRes.json() : { timestamp: at, powerKw: 0 }
            const predicted = predictRes && predictRes.ok ? (await predictRes.json())[0] : null
            return { id: d.id, timestamp, powerKw, predicted }
          })
        ),
        gridNodeId ? fetch(`/grid/${gridNodeId}/live?at=${at}`) : Promise.resolve(null),
      ])

      let total = 0
      let totalPredicted = 0
      const nextPowers: Record<number, number> = {}
      const cutoff = simTimeRef.current.getTime() - LIVE_HISTORY_MS
      for (const r of results) {
        nextPowers[r.id] = r.powerKw
        const arr = dataByDevice.current.get(r.id) ?? []
        arr.push({ x: new Date(r.timestamp).getTime(), y: r.powerKw })
        while (arr.length && arr[0].x < cutoff) arr.shift()
        dataByDevice.current.set(r.id, arr)
        total += r.powerKw

        if (r.predicted) {
          const parr = predictedDataByDevice.current.get(r.id) ?? []
          parr.push({ x: new Date(r.predicted.timestamp).getTime(), y: r.predicted.predictedKw })
          while (parr.length && parr[0].x < cutoff) parr.shift()
          predictedDataByDevice.current.set(r.id, parr)
          totalPredicted += r.predicted.predictedKw
        }
      }
      setPowers(nextPowers)
      totalDataRef.current.push({ x: simTimeRef.current.getTime(), y: total })
      while (totalDataRef.current.length && totalDataRef.current[0].x < cutoff) totalDataRef.current.shift()
      totalPredictedRef.current.push({ x: simTimeRef.current.getTime(), y: totalPredicted })
      while (totalPredictedRef.current.length && totalPredictedRef.current[0].x < cutoff) totalPredictedRef.current.shift()

      if (priceRes && priceRes.ok) {
        const { timestamp, powerKw: price } = await priceRes.json()
        priceDataRef.current.push({ x: new Date(timestamp).getTime(), y: price })
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
          ...devices.map((d, i) => ({
            label: `${d.name} (predicted)`,
            data: predictedDataByDevice.current.get(d.id) ?? [],
            borderColor: COLORS[i % COLORS.length],
            borderDash: [4, 4],
            borderWidth: 1,
            pointRadius: 0,
            hidden: !visible[d.id],
          })),
          {
            label: 'Total',
            data: totalDataRef.current,
            borderColor: '#FF0000',
            borderWidth: 2,
            pointRadius: 0,
            hidden: !visible['total'],
          },
          {
            label: 'Total (predicted)',
            data: totalPredictedRef.current,
            borderColor: '#FF0000',
            borderDash: [4, 4],
            borderWidth: 2,
            pointRadius: 0,
            hidden: !visible['total'],
          },
        ]
        chartRef.current.options.scales!.x!.min = simTimeRef.current.getTime() - LIVE_HISTORY_MS
        chartRef.current.options.scales!.x!.max = simTimeRef.current.getTime()
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
      // Timestamped on the same shared simulated clock as the power/price charts, not
      // real wall-clock time — this data reflects real backend ticks (SoC/Mode can't be
      // fast-forwarded), but plotting it on the simulated timeline is what makes it
      // line up with everything else instead of running on its own separate axis.
      const now = currentSimTimeMs()
      const cutoff = now - LIVE_HISTORY_MS

      setInverterMode(status.inverterMode)
      setNetGridKw(status.netGridKw)
      setGeneratedKw(status.generatedKw)
      setAcConsumption(status.acConsumption)
      setGeneratedAcKw(status.generatedAcKw)
      setAllAccumulators(status.accumulators)
      setActiveAccPriority(status.activeAccPriority)
      setInverterCurrent(status.inverterCurrent)
      setActiveAccumulator(status.accumulators.find((a) => a.mode === 'Charging' || a.mode === 'Draining') ?? null)
      setScenario(status.scenario)

      gridPowerDataRef.current.push({ x: now, y: status.inverterCurrent })
      while (gridPowerDataRef.current.length && gridPowerDataRef.current[0].x < cutoff) gridPowerDataRef.current.shift()

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


  async function handleAnalyze() {
    for (const d of devices) {
      addLog(`Analyzing device ${d.id}...`)
      const res = await fetch(`/devices/${d.id}/analyze`, { method: 'POST' })
      if (!res.ok) { addLog(`Analyze failed for device ${d.id}: ${res.status} ${await res.text()}`); continue }
    }
    predictEnabledRef.current = true
    addLog('Prediction enabled — now plotting alongside live data.')
  }

  return (
    <div>
      <label>
        Sim hours/tick{' '}
        <input type="number" step="0.01" value={simHoursPerTickText} onChange={(e) => setSimHoursPerTickText(e.target.value)} />
      </label>
      <button onClick={handleAnalyze}>Analyze (Live)</button>
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
                checked={visible['total'] ?? true}
                onChange={(e) => setVisible((v) => ({ ...v, total: e.target.checked }))}
              />
              {' '}Total
            </label>
          </div>
          <div style={{ height: '500px' }}>
            <canvas ref={chartCanvasRef}></canvas>
          </div>
        </div>

        {/* Right column: everything else */}
        <div style={{ flex: 1, minWidth: 0 }}>
          <h3>Power Price</h3>
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
          <p>Scenario: <strong>{scenario || '—'}</strong></p>
          <p>Net grid power: {netGridKw.toFixed(2)} kW ({inverterMode || '—'})</p>
          <p>generatedKw: {generatedKw.toFixed(2)} kW | generatedKw*dc2acEfficiency: {generatedAcKw.toFixed(2)} kW | acConsumption: {acConsumption.toFixed(2)} kW</p>
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
