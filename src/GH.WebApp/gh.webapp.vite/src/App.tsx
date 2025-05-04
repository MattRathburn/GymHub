import { Routes, Route } from 'react-router';
import './App.css'
import { Layout } from './components/Layout/Layout';
import { Home } from './components/Home/Home';
import { UserSession } from './components/UserSession/UserSession';

function App() {

  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Home />} />
        <Route path="/user-session" element={<UserSession />} />
      </Route>
    </Routes>
  )
}

export default App
