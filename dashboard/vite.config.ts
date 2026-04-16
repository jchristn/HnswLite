import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import pkg from './package.json';

export default defineConfig({
  base: '/dashboard/',
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
    __HNSWLITE_SERVER_URL__: JSON.stringify(process.env.HNSWLITE_SERVER_URL || ''),
  },
  build: {
    chunkSizeWarningLimit: 1000,
  },
  server: {
    port: 3000,
    proxy: {
      '/v1.0': {
        target: process.env.HNSWLITE_SERVER_URL || 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
});
