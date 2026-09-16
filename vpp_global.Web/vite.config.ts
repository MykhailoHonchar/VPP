import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
  proxy: {
      '/devices': 'http://localhost:5019',
      '/home-systems':  'http://localhost:5019',
      '/grid/': 'http://localhost:5019',
      '/simulation': 'http://localhost:5019',
    },
  },
})
