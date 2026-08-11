import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The build output is embedded into SimpleDlna.Admin as wwwroot/**, so asset
// paths must be absolute from the server root and hashed names must land under
// /assets/ - WebAssets serves that prefix with a one-year immutable cache.
export default defineConfig({
  plugins: [react()],
  base: '/',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsDir: 'assets',
    sourcemap: false,
    // One bundle keeps the embedded resource list short and the app is small.
    chunkSizeWarningLimit: 700,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:19199',
        // changeOrigin stays false so the Origin header keeps a loopback
        // host and the API's origin check accepts it.
        changeOrigin: false,
      },
    },
  },
})
