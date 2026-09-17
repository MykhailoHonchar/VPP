import { useEffect, useState } from 'react'
import { useParams, Link } from 'react-router-dom'

interface GodSimulation {
  id: number
  meanKw: number
  amplitudeKw: number
  periodHours: number
  phaseShift: number
  noiseStdDevKw: number
  allowNegative: boolean
}

interface GodDevice {
  id: number
  name: string
  type: string
  currentChargeKWH: number | null
  capacityKWH: number | null
  lowKWH: number | null
  maxKWH: number | null
  minKWH: number | null
  priority: number | null
  mode: string | null
  simulations: GodSimulation[]
}

interface GodData {
  homeSystem: { id: number; name: string; gridNodeId: number; lowPrice: number; highPrice: number }
  devices: GodDevice[]
  priceSimulations: GodSimulation[]
}

type SimForm = { meanKw: string; amplitudeKw: string; periodHours: string; phaseShift: string; noiseStdDevKw: string; allowNegative: boolean }

const simToForm = (s: GodSimulation): SimForm => ({
  meanKw: String(s.meanKw),
  amplitudeKw: String(s.amplitudeKw),
  periodHours: String(s.periodHours),
  phaseShift: String(s.phaseShift),
  noiseStdDevKw: String(s.noiseStdDevKw),
  allowNegative: s.allowNegative,
})

function SimulationEditor({ label, sim, onSaved }: { label: string; sim: GodSimulation; onSaved: () => void }) {
  const [form, setForm] = useState<SimForm>(simToForm(sim))
  const [savedAt, setSavedAt] = useState<number | null>(null)
  const set = <K extends keyof SimForm>(key: K, value: SimForm[K]) => setForm((f) => ({ ...f, [key]: value }))

  async function save() {
    await fetch(`/simulations/${sim.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        meanKw: Number(form.meanKw) || 0,
        amplitudeKw: Number(form.amplitudeKw) || 0,
        periodHours: Number(form.periodHours) || 0,
        phaseShift: Number(form.phaseShift) || 0,
        noiseStdDevKw: Number(form.noiseStdDevKw) || 0,
        allowNegative: form.allowNegative,
      }),
    })
    setSavedAt(Date.now())
    onSaved()
  }

  return (
    <div style={{ border: '1px solid var(--border)', borderRadius: '6px', padding: '0.5rem 0.75rem', marginBottom: '0.5rem' }}>
      <div style={{ fontSize: '0.85em', opacity: 0.8, marginBottom: '0.35rem' }}>{label} (#{sim.id})</div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', alignItems: 'center' }}>
        <label>Mean kW <input type="number" step="0.1" value={form.meanKw} onChange={(e) => set('meanKw', e.target.value)} style={{ width: '70px' }} /></label>
        <label>Amplitude kW <input type="number" step="0.1" value={form.amplitudeKw} onChange={(e) => set('amplitudeKw', e.target.value)} style={{ width: '70px' }} /></label>
        <label>Period h <input type="number" step="1" value={form.periodHours} onChange={(e) => set('periodHours', e.target.value)} style={{ width: '70px' }} /></label>
        <label>Phase shift <input type="number" step="1" value={form.phaseShift} onChange={(e) => set('phaseShift', e.target.value)} style={{ width: '70px' }} /></label>
        <label>Noise σ kW <input type="number" step="0.05" value={form.noiseStdDevKw} onChange={(e) => set('noiseStdDevKw', e.target.value)} style={{ width: '70px' }} /></label>
        <label>
          <input type="checkbox" checked={form.allowNegative} onChange={(e) => set('allowNegative', e.target.checked)} />
          {' '}Allow negative
        </label>
        <button onClick={save}>Save</button>
        {savedAt && <span style={{ fontSize: '0.8em', opacity: 0.7 }}>Saved</span>}
      </div>
    </div>
  )
}

function GodPage() {
  const { id } = useParams<{ id: string }>()
  const homeSystemId = Number(id)

  const [data, setData] = useState<GodData | null>(null)
  const [error, setError] = useState('')

  const [priceForm, setPriceForm] = useState<{ lowPrice: string; highPrice: string } | null>(null)
  const [priceSavedAt, setPriceSavedAt] = useState<number | null>(null)

  type AccForm = { currentChargeKWH: string; capacityKWH: string; lowKWH: string; maxKWH: string; minKWH: string; priority: string }
  const [accForms, setAccForms] = useState<Record<number, AccForm>>({})
  const [accSavedAt, setAccSavedAt] = useState<Record<number, number>>({})

  const load = () => {
    fetch(`/home-systems/${homeSystemId}/god`)
      .then((res) => (res.ok ? res.json() : Promise.reject(res.status)))
      .then((god: GodData) => {
        setData(god)
        setPriceForm({ lowPrice: String(god.homeSystem.lowPrice), highPrice: String(god.homeSystem.highPrice) })
        const nextAccForms: Record<number, AccForm> = {}
        for (const d of god.devices) {
          if (d.currentChargeKWH === null) continue
          nextAccForms[d.id] = {
            currentChargeKWH: String(d.currentChargeKWH),
            capacityKWH: String(d.capacityKWH),
            lowKWH: String(d.lowKWH),
            maxKWH: String(d.maxKWH),
            minKWH: String(d.minKWH),
            priority: String(d.priority),
          }
        }
        setAccForms(nextAccForms)
      })
      .catch(() => setError('Failed to load — is the home system id correct and the backend running?'))
  }

  useEffect(load, [homeSystemId])

  async function saveHomeSystem() {
    if (!priceForm) return
    await fetch(`/home-systems/${homeSystemId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ lowPrice: Number(priceForm.lowPrice) || 0, highPrice: Number(priceForm.highPrice) || 0 }),
    })
    setPriceSavedAt(Date.now())
  }

  const updateAccField = (deviceId: number, field: keyof AccForm, value: string) =>
    setAccForms((m) => ({ ...m, [deviceId]: { ...m[deviceId], [field]: value } }))

  async function saveAccumulator(deviceId: number) {
    const f = accForms[deviceId]
    if (!f) return
    await fetch(`/devices/${deviceId}/accumulator`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        currentChargeKWH: Number(f.currentChargeKWH) || 0,
        capacityKWH: Number(f.capacityKWH) || 0,
        lowKWH: Number(f.lowKWH) || 0,
        maxKWH: Number(f.maxKWH) || 0,
        minKWH: Number(f.minKWH) || 0,
        priority: Number(f.priority) || 0,
      }),
    })
    setAccSavedAt((m) => ({ ...m, [deviceId]: Date.now() }))
  }

  if (error) return <div><p>{error}</p></div>
  if (!data || !priceForm) return <div><p>Loading...</p></div>

  return (
    <div>
      <p><Link to={`/homesystem/${homeSystemId}`}>&larr; Back to Home System {homeSystemId}</Link></p>
      <h2>God Mode — Home System {homeSystemId}</h2>

      <h3>Home System</h3>
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: '1rem' }}>
        <label>Low price <input type="number" step="0.01" value={priceForm.lowPrice} onChange={(e) => setPriceForm({ ...priceForm, lowPrice: e.target.value })} style={{ width: '80px' }} /></label>
        <label>High price <input type="number" step="0.01" value={priceForm.highPrice} onChange={(e) => setPriceForm({ ...priceForm, highPrice: e.target.value })} style={{ width: '80px' }} /></label>
        <button onClick={saveHomeSystem}>Save</button>
        {priceSavedAt && <span style={{ fontSize: '0.8em', opacity: 0.7 }}>Saved</span>}
      </div>

      <h3>Grid price simulation</h3>
      {data.priceSimulations.map((sim, i) => (
        <SimulationEditor key={sim.id} label={`Price component ${i + 1}`} sim={sim} onSaved={() => {}} />
      ))}

      <h3>Devices</h3>
      {data.devices.map((d) => (
        <div key={d.id} style={{ border: '1px solid var(--border)', borderRadius: '8px', padding: '0.75rem', marginBottom: '1rem' }}>
          <h4 style={{ margin: '0 0 0.5rem' }}>{d.name} ({d.type}) — #{d.id}</h4>

          {d.currentChargeKWH !== null && accForms[d.id] && (
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem' }}>
              <label>Current charge kWh <input type="number" step="1" value={accForms[d.id].currentChargeKWH} onChange={(e) => updateAccField(d.id, 'currentChargeKWH', e.target.value)} style={{ width: '80px' }} /></label>
              <label>Capacity kWh <input type="number" step="1" value={accForms[d.id].capacityKWH} onChange={(e) => updateAccField(d.id, 'capacityKWH', e.target.value)} style={{ width: '80px' }} /></label>
              <label>Low kWh <input type="number" step="1" value={accForms[d.id].lowKWH} onChange={(e) => updateAccField(d.id, 'lowKWH', e.target.value)} style={{ width: '80px' }} /></label>
              <label>Max kWh <input type="number" step="1" value={accForms[d.id].maxKWH} onChange={(e) => updateAccField(d.id, 'maxKWH', e.target.value)} style={{ width: '80px' }} /></label>
              <label>Min kWh <input type="number" step="1" value={accForms[d.id].minKWH} onChange={(e) => updateAccField(d.id, 'minKWH', e.target.value)} style={{ width: '80px' }} /></label>
              <label>Priority <input type="number" step="1" value={accForms[d.id].priority} onChange={(e) => updateAccField(d.id, 'priority', e.target.value)} style={{ width: '60px' }} /></label>
              <span style={{ fontSize: '0.85em', opacity: 0.8 }}>Mode: {d.mode}</span>
              <button onClick={() => saveAccumulator(d.id)}>Save</button>
              {accSavedAt[d.id] && <span style={{ fontSize: '0.8em', opacity: 0.7 }}>Saved</span>}
            </div>
          )}

          {d.simulations.length === 0 ? (
            <p style={{ fontSize: '0.85em', opacity: 0.7 }}>No simulation curve on this device.</p>
          ) : (
            d.simulations.map((sim, i) => (
              <SimulationEditor key={sim.id} label={`Curve component ${i + 1}`} sim={sim} onSaved={() => {}} />
            ))
          )}
        </div>
      ))}
    </div>
  )
}

export default GodPage
