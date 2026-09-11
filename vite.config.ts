import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath, URL } from 'node:url';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  // RxDB and some deps reference `global`; map it to the browser global.
  define: {
    global: 'globalThis',
  },
  optimizeDeps: {
    // pdfjs ships an ESM worker that Vite should not try to pre-bundle.
    exclude: ['pdfjs-dist'],
  },
  build: {
    // The largest chunks (TipTap, jsPDF) are lazy-loaded on demand.
    chunkSizeWarningLimit: 900,
    rollupOptions: {
      output: {
        // Split big vendors into their own cacheable chunks.
        manualChunks: {
          rxdb: ['rxdb'],
          tiptap: [
            '@tiptap/react',
            '@tiptap/starter-kit',
            '@tiptap/extension-link',
            '@tiptap/extension-underline',
            '@tiptap/extension-placeholder',
            'tiptap-markdown',
          ],
        },
      },
    },
  },
});
