import { useState, useEffect } from 'react';
import Login from './pages/Login';
import Chat from './pages/Chat';
import './index.css';

function App() {
  const [isLoggedIn, setIsLoggedIn] = useState(false);

  useEffect(() => {
    const token = localStorage.getItem('token');
    setIsLoggedIn(!!token);
  }, []);

  const handleLogin = () => {
    setIsLoggedIn(true);
  };

  const handleLogout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    setIsLoggedIn(false);
  };

  return (
    <div className="min-h-screen">
      {!isLoggedIn ? (
        <Login onLogin={handleLogin} />
      ) : (
        <Chat onLogout={handleLogout} />
      )}
    </div>
  );
}

export default App;
