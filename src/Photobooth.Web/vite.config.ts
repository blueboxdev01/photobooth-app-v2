import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Builds straight into the server's wwwroot so a published .exe serves the UI
// with no extra step. wwwroot is gitignored -- it is build output.
export default defineConfig({
  plugins: [react()],

  // The guest screen carries real logic now -- which shot to hold on screen, and
  // when -- and Smart App Control blocks running the booth locally on the
  // development machine, so CI is where that logic actually gets exercised.
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx'],
  },
  build: {
    outDir: '../Photobooth.Server/wwwroot',
    emptyOutDir: true,
  },
  server: {
    // `npm run dev` proxies to the running backend for frontend-only work.
    proxy: {
      '/api': 'http://localhost:8080',
      '/hub': { target: 'http://localhost:8080', ws: true },
    },
  },
})
