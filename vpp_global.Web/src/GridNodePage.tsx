import { useState, useRef, useEffect } from 'react'
import { useParams } from "react-router-dom"

import { Chart } from 'chart.js/auto'
import zoomPlugin from 'chartjs-plugin-zoom'

Chart.register(zoomPlugin)

const LIVE_HISTORY_MS = 50 * 24 * 3600 * 1000

function GridNodePage()
{
  const [simHoursPerTick, setSimHoursPerTick] = useState(1)
  const simTimeRef = useRef<Date>(new Date())

  const [days, setDays] = useState(30)
  const [log, setLog] = useState('')
  const addLog = (msg: string) => setLog((prev) => prev + msg + '\n')

  const chartCanvasRef = useRef<HTMLCanvasElement>(null)
  const chartRef = useRef<Chart | null>(null)
  const liveDataRef = useRef<{ x: number; y: number }[]>([])
  const predictedDataRef = useRef<{ x: number; y: number }[]>([])
  const predictEnabledRef = useRef(false)

  const { id } = useParams<{ id: string }>()
  const gridNodeId = Number(id)

  const [powerKw, setPowerKw] = useState(0)



  useEffect(() => {
    if (chartRef.current) chartRef.current.destroy()
    liveDataRef.current = []
    predictedDataRef.current = []
    predictEnabledRef.current = false
    simTimeRef.current = new Date()

    chartRef.current = new Chart(chartCanvasRef.current!, {
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
          y: { min: -5, max: 5, title: { display: true, text: 'kW' } },
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
    const tick = async () => {

      simTimeRef.current = new Date(simTimeRef.current.getTime() + simHoursPerTick * 3600 * 1000)
      const at = simTimeRef.current.toISOString()

      const [liveRes, predictRes] = await Promise.all([
        fetch(`/grid/${gridNodeId}/live?at=${at}`),
        predictEnabledRef.current ? fetch(`/grid/${gridNodeId}/predict?from=${at}&to=${at}`) : Promise.resolve(null),
      ])
      const { timestamp, powerKw: kw } = liveRes.ok ? await liveRes.json() : { timestamp: at, powerKw: 0 }
      const predicted = predictRes && predictRes.ok ? (await predictRes.json())[0] : null

      const cutoff = simTimeRef.current.getTime() - LIVE_HISTORY_MS

      setPowerKw(kw)
      liveDataRef.current.push({ x: new Date(timestamp).getTime(), y: kw })
      while (liveDataRef.current.length && liveDataRef.current[0].x < cutoff) liveDataRef.current.shift()

      if (predicted) {
        predictedDataRef.current.push({ x: new Date(predicted.timestamp).getTime(), y: predicted.predictedKw })
        while (predictedDataRef.current.length && predictedDataRef.current[0].x < cutoff) predictedDataRef.current.shift()
      }

      if (chartRef.current) {
        chartRef.current.data.datasets = [
          {
            label: 'Grid power',
            data: liveDataRef.current,
            borderColor: '#2563eb',
            borderWidth: 2,
            pointRadius: 0,
          },
          {
            label: 'Grid power (predicted)',
            data: predictedDataRef.current,
            borderColor: '#2563eb',
            borderDash: [4, 4],
            borderWidth: 2,
            pointRadius: 0,
          },
        ]
        chartRef.current.options.scales!.x!.min = simTimeRef.current.getTime() - LIVE_HISTORY_MS
        chartRef.current.options.scales!.x!.max = simTimeRef.current.getTime()
        chartRef.current.update()
      }
    }

    tick()
    const interval = setInterval(tick, 200)
    return () => clearInterval(interval)
  }, [gridNodeId, simHoursPerTick])


  async function handleGenerate() {
    addLog(`Generating ${days} days of readings for grid node ${gridNodeId}...`)
    const res = await fetch(`/grid/${gridNodeId}/readings/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ days }),
    })
    addLog(res.ok ? 'Done.' : `Failed: ${res.status} ${await res.text()}`)
  }

  async function handleAnalyze() {
    addLog(`Analyzing grid node ${gridNodeId}...`)
    const res = await fetch(`/grid/${gridNodeId}/analyze`, { method: 'POST' })
    if (!res.ok) { addLog(`Analyze failed: ${res.status} ${await res.text()}`); return }
    predictEnabledRef.current = true
    addLog('Prediction enabled — now plotting alongside live data.')
  }

  return (
    <div>
      <label>
        Backfill days{' '}
        <input type="number" value={days} onChange={(e) => setDays(Number(e.target.value))} />
      </label>
      <button onClick={handleGenerate}>Generate Data</button>
      <button onClick={handleAnalyze}>Analyze (Live)</button>
      <label>
        Sim hours/tick{' '}
        <input type="number" step="0.5" value={simHoursPerTick} onChange={(e) => setSimHoursPerTick(Number(e.target.value))} />
      </label>
      <h2>Grid Node {gridNodeId}</h2>
      <p>Power: {powerKw.toFixed(2)} kW</p>
      <canvas ref={chartCanvasRef} height={100}></canvas>
      <pre>{log}</pre>
    </div>
  )
}

export default GridNodePage
