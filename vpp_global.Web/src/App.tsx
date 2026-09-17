import { useState, useRef, useEffect } from 'react'
import{ Chart } from 'chart.js/auto'
import zoomPlugin from 'chartjs-plugin-zoom'
import './App.css'
import { COLORS } from './colors'

Chart.register(zoomPlugin)

function App() {

  
  const [deviceIdInput, setDeviceIdInput] = useState('3,5,7')
  const deviceIds = deviceIdInput.split(',').map(s => Number(s.trim())).filter(n => !isNaN(n))
  const [days, setDays] = useState(30)
  const [log, setLog] = useState('')

  const chartCanvasRef = useRef<HTMLCanvasElement>(null)
  const chartRef = useRef<Chart | null>(null)

  const [isLive, setIsLive] = useState(false)
  const [simHoursPerTick, setSimHoursPerTick] = useState(1)
  const [liveValue, setLiveValue] = useState('')

  const liveChartCanvasRef = useRef<HTMLCanvasElement>(null)
  const liveChartRef = useRef<Chart | null>(null)
  const simTimeRef = useRef<Date>(new Date())

  const LIVE_HISTORY_MS = 50 * 24 * 3600 * 1000

  const liveDataByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  const predictedDataByDevice = useRef<Map<number, { x: number; y: number }[]>>(new Map())
  const totalLiveRef = useRef<{ x: number; y: number }[]>([])
  const totalPredictedRef = useRef<{ x: number; y: number }[]>([])

  const predictEnabledRef = useRef(false)

  const addLog = (msg: string) => setLog((prev) => prev + msg + '\n')

  async function handleLoadGraph() {
    const graphDays = 50
    const now = Date.now()
    const since = now - graphDays * 24 * 3600 * 1000

    const results = await Promise.all(
      deviceIds.map(async (id) => {
        const [readingsRes, predictRes] = await Promise.all([
          fetch(`/devices/${id}/readings?days=${graphDays}`),
          fetch(`/devices/${id}/predict?days=${graphDays}`),
        ])
        const readings = readingsRes.ok ? await readingsRes.json() : []
        const predicted = predictRes.ok ? await predictRes.json() : []
        return { id, readings, predicted }
      })
    )

    // Bucket by hour (rounded) rather than exact timestamp — different devices' backfills
    // run a few seconds apart, so their raw timestamps don't line up exactly.
    const totalActualMap = new Map<number, number>()
    const totalPredictedMap = new Map<number, number>()
    const roundToHour = (iso: string) => Math.round(new Date(iso).getTime() / 3600000) * 3600000

    for (const { readings, predicted } of results) {
      for (const r of readings) {
        const t = roundToHour(r.timestamp)
        totalActualMap.set(t, (totalActualMap.get(t) ?? 0) + r.powerKw)
      }
      for (const p of predicted) {
        const t = roundToHour(p.timestamp)
        totalPredictedMap.set(t, (totalPredictedMap.get(t) ?? 0) + p.predictedKw)
      }
    }
    const totalActual = [...totalActualMap.entries()].sort((a, b) => a[0] - b[0]).map(([x, y]) => ({ x, y }))
    const totalPredicted = [...totalPredictedMap.entries()].sort((a, b) => a[0] - b[0]).map(([x, y]) => ({ x, y }))

    if (chartRef.current) chartRef.current.destroy()

    chartRef.current = new Chart(chartCanvasRef.current!, {
      type: 'line',
      data: {
        datasets: [
          ...results.map(({ id, readings }, i) => ({
            label: `Device ${id} (actual)`,
            data: readings.map((r: any) => ({ x: new Date(r.timestamp).getTime(), y: r.powerKw })),
            borderColor: COLORS[i % COLORS.length],
            borderWidth: 1,
            pointRadius: 0,
          })),
          ...results.map(({ id, predicted }, i) => ({
            label: `Device ${id} (predicted)`,
            data: predicted.map((p: any) => ({ x: new Date(p.timestamp).getTime(), y: p.predictedKw })),
            borderColor: COLORS[i % COLORS.length],
            borderDash: [4, 4],
            borderWidth: 1,
            pointRadius: 0,
          })),
          { label: 'Total (actual)', data: totalActual, borderColor: '#FF0000', borderWidth: 2, pointRadius: 0 },
          { label: 'Total (predicted)', data: totalPredicted, borderColor: '#FF0000', borderDash: [4, 4], borderWidth: 2, pointRadius: 0 },
        ],
      },
      options: {
        parsing: false,
        scales: {
          x: { type: 'linear', min: since, max: now, ticks: { callback: (v) => new Date(v as number).toLocaleDateString() } },
          y: { min: -20, max: 20, title: { display: true, text: 'kW' } },
        },
        plugins: {
          zoom: {
            pan: { enabled: true, mode: 'x' },
            zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' },
          },
        },
      },
    })
  }

  async function handleGenerate() {
    for (const id of deviceIds) {
      addLog(`Generating ${days} days of readings for device ${id}...`)
      const res = await fetch(`/devices/${id}/readings/generate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ days }),
      })
      addLog(res.ok ? 'Done.' : `Failed for device ${id}: ${res.status} ${await res.text()}`)
    }
  }

  async function handleAnalyze() {
    for (const id of deviceIds) {
      addLog(`Analyzing device ${id}...`)
      const res = await fetch(`/devices/${id}/analyze`, { method: 'POST' })
      addLog(res.ok ? 'Done.' : `Failed for device ${id}: ${res.status} ${await res.text()}`)
    }
  }

  useEffect(() => {
    if (!isLive) return

    if (liveChartRef.current) liveChartRef.current.destroy()
    liveDataByDevice.current.clear()
    predictedDataByDevice.current.clear()
    totalLiveRef.current = []
    totalPredictedRef.current = []
    predictEnabledRef.current = false
    simTimeRef.current = new Date()

    liveChartRef.current = new Chart(liveChartCanvasRef.current!, {
      type: 'line',
      data: { datasets: [] },
      options: {
        parsing: false,
        animation: false,
        scales: {
          x: {
            type: 'linear',
            min: simTimeRef.current.getTime() - LIVE_HISTORY_MS,
            max: simTimeRef.current.getTime(),
            ticks: { callback: (v) => new Date(v as number).toLocaleDateString() },
          },
          y: { min: -20, max: 20, title: { display: true, text: 'kW' } },
        },
        plugins: {
          zoom: {
            pan: { enabled: true, mode: 'x' },
            zoom: { wheel: { enabled: true }, pinch: { enabled: true }, mode: 'x' },
          },
        },
      },
    })

    // No `at` is sent for /live — each call gets back the backend's own authoritative
    // SimulationClock reading instead of a client-guessed one (see HomeSystemPage.tsx's
    // tick() for why: a guessed `at` that fell behind an accumulator's last-committed
    // ramp tick made the live read return a frozen value instead of a genuinely live
    // one). /predict then asks about that same returned instant, not a separate guess.
    const tick = async () => {
      const results = await Promise.all(
        deviceIds.map(async (id) => {
          const liveRes = await fetch(`/devices/${id}/live`)
          const live = liveRes.ok ? await liveRes.json() : null
          const predicted = predictEnabledRef.current && live
            ? await fetch(`/devices/${id}/predict?from=${live.timestamp}&to=${live.timestamp}`)
                .then((r) => (r.ok ? r.json() : null))
                .then((arr) => arr?.[0] ?? null)
            : null
          return { id, live, predicted }
        })
      )

      simTimeRef.current = new Date(results.find((r) => r.live)?.live.timestamp ?? simTimeRef.current)
      const at = simTimeRef.current.toISOString()

      let totalLive = 0
      let totalPredicted = 0
      const cutoff = simTimeRef.current.getTime() - LIVE_HISTORY_MS

      for (const { id, live, predicted } of results) {
        if (live) {
          const arr = liveDataByDevice.current.get(id) ?? []
          arr.push({ x: new Date(live.timestamp).getTime(), y: live.powerKw })
          while (arr.length && arr[0].x < cutoff) arr.shift()
          liveDataByDevice.current.set(id, arr)
          totalLive += live.powerKw
        }
        if (predicted) {
          const arr = predictedDataByDevice.current.get(id) ?? []
          arr.push({ x: new Date(predicted.timestamp).getTime(), y: predicted.predictedKw })
          while (arr.length && arr[0].x < cutoff) arr.shift()
          predictedDataByDevice.current.set(id, arr)
          totalPredicted += predicted.predictedKw
        }
      }

      totalLiveRef.current.push({ x: simTimeRef.current.getTime(), y: totalLive })
      while (totalLiveRef.current.length && totalLiveRef.current[0].x < cutoff) totalLiveRef.current.shift()
      totalPredictedRef.current.push({ x: simTimeRef.current.getTime(), y: totalPredicted })
      while (totalPredictedRef.current.length && totalPredictedRef.current[0].x < cutoff) totalPredictedRef.current.shift()

      setLiveValue(`Total: ${totalLive.toFixed(2)} kW  (sim: ${new Date(at).toLocaleString()})`)

      if (liveChartRef.current) {
        liveChartRef.current.data.datasets = [
          ...deviceIds.map((id, i) => ({
            label: `Device ${id} (live)`,
            data: liveDataByDevice.current.get(id) ?? [],
            borderColor: COLORS[i % COLORS.length],
            borderWidth: 1,
            pointRadius: 0,
          })),
          ...deviceIds.map((id, i) => ({
            label: `Device ${id} (predicted)`,
            data: predictedDataByDevice.current.get(id) ?? [],
            borderColor: COLORS[i % COLORS.length],
            borderDash: [4, 4],
            borderWidth: 1,
            pointRadius: 0,
          })),
          { label: 'Total (live)', data: totalLiveRef.current, borderColor: '#FF0000', borderWidth: 2, pointRadius: 0 },
          { label: 'Total (predicted)', data: totalPredictedRef.current, borderColor: '#FF0000', borderDash: [4, 4], borderWidth: 2, pointRadius: 0 },
        ]
        liveChartRef.current.options.scales!.x!.min = simTimeRef.current.getTime() - LIVE_HISTORY_MS
        liveChartRef.current.options.scales!.x!.max = simTimeRef.current.getTime()
        liveChartRef.current.update()
      }
    }

    // Self-scheduling instead of setInterval: waits for one tick to fully finish before
    // scheduling the next, so a slow round-trip can't let two ticks overlap and resolve
    // out of order (same reasoning as HomeSystemPage.tsx's tick loop).
    let cancelled = false
    let timeoutId: ReturnType<typeof setTimeout>
    const scheduleNext = () => { if (!cancelled) timeoutId = setTimeout(runTick, 200) }
    const runTick = () => { tick().finally(scheduleNext) }
    runTick()
    return () => { cancelled = true; clearTimeout(timeoutId) }
    // deviceIds is re-derived from deviceIdInput every render (new array reference each
    // time), so depending on it directly would tear down and rebuild this effect on
    // every render; deviceIdInput already covers the same change. LIVE_HISTORY_MS is a
    // module-level constant, never changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLive, deviceIdInput])

  // The backend's SimulationClock is a global singleton shared by every page — posting
  // here keeps this page's speed control consistent with HomeSystemPage.tsx's, since
  // /live no longer takes a client-guessed `at` for this page to drive itself.
  useEffect(() => {
    fetch('/simulation/speed', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ hoursPerTick: simHoursPerTick }),
    })
  }, [simHoursPerTick])

  async function handleLiveAnalyze() {
    for (const id of deviceIds) {
      addLog(`Analyzing device ${id}...`)
      const res = await fetch(`/devices/${id}/analyze`, { method: 'POST' })
      if (!res.ok) { addLog(`Analyze failed for device ${id}: ${res.status} ${await res.text()}`); continue }
    }
    predictEnabledRef.current = true
    addLog('Prediction enabled — now plotting alongside live data.')
  }

  return (
    <div>
      <div className="row">
        <label>
          Device IDs (comma-separated){' '}
          <input type="text" value={deviceIdInput} onChange={(e) => setDeviceIdInput(e.target.value)} />
        </label>
        <label>
          Backfill days{' '}
          <input type="number" value={days} onChange={(e) => setDays(Number(e.target.value))} />
        </label>
        <button onClick={handleGenerate}>Generate Data</button>
        <button onClick={handleAnalyze}>Analyze</button>
        <button onClick={handleLoadGraph}>Load Graph</button>
        <button onClick={() => setIsLive((v) => !v)}>{isLive ? 'Stop Live' : 'Start Live'}</button>
        <button onClick={handleLiveAnalyze}>Analyze (Live)</button>
        <label>
          Sim hours/tick{' '}
          <input type="number" step="0.5" value={simHoursPerTick} onChange={(e) => setSimHoursPerTick(Number(e.target.value))} />
        </label>
        <span>{liveValue}</span>
      </div>
      <canvas ref={chartCanvasRef} height={100}></canvas>
      <canvas ref={liveChartCanvasRef} height={100}></canvas>
      <pre className="log">{log}</pre>
    </div>
  )
}

export default App
