import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  return {
    plugins: [react()],
    server: {
      port: parseInt(env.VITE_PORT),
      proxy: {
        '/api': {
          target: process.env.services__ghidentityapi__https__0 ||
            process.env.services__ghidentityapi__http__0,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/api/, ''),
            secure: false,
        },
        '^/bff': {
          target: process.env.services__ghidentityapi__https__0 ||
            process.env.services__ghidentityapi__http__0,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/bff/, ''),
            secure: false,
        },
        '^/signin-oidc': {
          target: process.env.services__ghidentityapi__https__0 ||
            process.env.services__ghidentityapi__http__0,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/signin-oidc/, ''),
            secure: false,
        },
        '^/signout-callback-oidc': {
          target: process.env.services__ghidentityapi__https__0 ||
            process.env.services__ghidentityapi__http__0,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/signout-callback-oidc/, ''),
            secure: false,
        },
        '^/todos': {
          target: process.env.services__ghidentityapi__https__0 ||
            process.env.services__ghidentityapi__http__0,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/todos/, ''),
            secure: false,
        },
      },
    },
    build: {
      outDir: 'dist',
      assetsDir: 'assets',
      sourcemap: true,
      rollupOptions: {
        input: {
          main: './index.html',
        },
      },
    },
  }

})
