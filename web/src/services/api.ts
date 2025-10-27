import axios from 'axios';

const API_URL = '/api';

const api = axios.create({
  baseURL: API_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export interface LoginRequest {
  Username: string;
  Password: string;
}

export interface RegisterRequest {
  Username: string;
  Email: string;
  Password: string;
  ConfirmPassword: string;
}

export interface ChatRequest {
  Message: string;
  ConversationId?: number;
}

export interface Conversation {
  Id: number;
  Title: string;
  AgentType: string;
  MessageCount: number;
  CreatedAt: string;
}

export interface ToolCallHistoryItem {
  Timestamp: string;
  Message: string;
}

export interface Message {
  Id: number;
  Role: string;
  Content: string;
  CreatedAt: string;
  ToolCallHistory?: ToolCallHistoryItem[];
}

export const authAPI = {
  login: (data: LoginRequest) => api.post('/auth/login', data),
  register: (data: RegisterRequest) => api.post('/auth/register', data),
};

export const conversationAPI = {
  create: (agentType: string, title?: string) =>
    api.post('/conversation/add', { AgentType: agentType, Title: title }),
  list: (pageIndex = 1, pageSize = 10) =>
    api.get(`/conversation/page?PageIndex=${pageIndex}&PageSize=${pageSize}`),
  getDetail: (id: number) => api.get(`/conversation/${id}`),
  delete: (id: number) => api.delete(`/conversation/delete/${id}`),
};

export const chatAPI = {
  sendMessage: (data: ChatRequest) => api.post('/chat/agent', data),
  clearHistory: (conversationId: number) => api.post(`/chat/clear/${conversationId}`),
};

export default api;
