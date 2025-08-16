import './App.css'
import { appRoutes } from './constants'
import { Home } from './components/routes/Home'
import { Routes, Route } from 'react-router-dom'
import { NotFound } from './components/routes/NotFound'
import { Playground } from './components/routes/Playground/Playground'
import { Layout } from './components/Layout'
import { useAuth } from 'react-oidc-context'
import { useEffect } from 'react'

function App() {
  const auth = useAuth();

  useEffect(() => {
    console.log('App: Auth state changed:', {
      isAuthenticated: auth.isAuthenticated,
      isLoading: auth.isLoading,
      user: auth.user,
      error: auth.error
    });
  }, [auth.isAuthenticated, auth.isLoading, auth.user, auth.error]);

  return (
    <Layout>
      <Routes>
        <Route path={appRoutes.home} element={<Home />} />
        <Route path={appRoutes.playground} element={<Playground />} />
        <Route path={appRoutes.notFound} element={<NotFound />} />
      </Routes>
    </Layout>
  )
}

export default App
