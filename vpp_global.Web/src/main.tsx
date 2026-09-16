import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import './index.css'
import App from './App.tsx'
import HomeSystemPage from './HomeSystemPage.tsx'
import GridNodePage from './GridNodePage.tsx'
import GodPage from './GodPage.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path = "/" element={<App />}/>
        <Route path = "/homesystem/:id" element={<HomeSystemPage />}/>
        <Route path = "/homesystem/:id/god" element={<GodPage />}/>
        <Route path = "/gridnode/:id" element={<GridNodePage />}/>
      </Routes>
    </BrowserRouter>
  </StrictMode>,
)
