import { useState } from 'react';
import { Sparkles, Heart } from 'lucide-react';
import { authAPI } from '../services/api';

interface LoginProps {
  onLogin: () => void;
}

export default function Login({ onLogin }: LoginProps) {
  const [isRegister, setIsRegister] = useState(false);
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    try {
      if (isRegister) {
        if (password !== confirmPassword) {
          setError('密码不匹配~');
          return;
        }
        await authAPI.register({ Username: username, Email: email, Password: password, ConfirmPassword: confirmPassword });
        alert('注册成功！请登录吧~ ✨');
        setIsRegister(false);
      } else {
        const res = await authAPI.login({ Username: username, Password: password });
        localStorage.setItem('token', res.data.Data.Token);
        localStorage.setItem('username', res.data.Data.Username);
        onLogin();
      }
    } catch (err: any) {
      setError(err.response?.data?.Message || '出错啦~ 请重试');
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center p-4">
      <div className="cute-card max-w-md w-full space-y-6 animate-bounce-slow">
        <div className="text-center">
          <div className="flex justify-center mb-4">
            <div className="bg-gradient-to-r from-pink-400 to-purple-400 p-4 rounded-full">
              <Sparkles className="w-12 h-12 text-white" />
            </div>
          </div>
          <h1 className="text-4xl font-bold bg-gradient-to-r from-pink-500 to-purple-500 bg-clip-text text-transparent">
            AgentHub
          </h1>
          <p className="text-gray-600 mt-2 flex items-center justify-center gap-1">
            你的专属AI助手 <Heart className="w-4 h-4 text-pink-400 fill-pink-400" />
          </p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <input
              type="text"
              placeholder="用户名"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="cute-input"
              required
            />
          </div>

          {isRegister && (
            <div>
              <input
                type="email"
                placeholder="邮箱"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="cute-input"
                required
              />
            </div>
          )}

          <div>
            <input
              type="password"
              placeholder="密码"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="cute-input"
              required
            />
          </div>

          {isRegister && (
            <div>
              <input
                type="password"
                placeholder="确认密码"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="cute-input"
                required
              />
            </div>
          )}

          {error && (
            <div className="text-center text-pink-500 text-sm bg-pink-50 rounded-lg p-2">
              {error}
            </div>
          )}

          <button type="submit" className="cute-button w-full">
            {isRegister ? '✨ 注册' : '💖 登录'}
          </button>
        </form>

        <div className="text-center">
          <button
            onClick={() => setIsRegister(!isRegister)}
            className="text-purple-500 hover:text-pink-500 transition-colors"
          >
            {isRegister ? '已有账号？去登录~' : '还没账号？快来注册吧~'}
          </button>
        </div>
      </div>
    </div>
  );
}
