import { useState, useEffect, useRef } from 'react';
import { Send, Plus, Sparkles, LogOut, MessageCircle, Menu, X, RotateCcw, StopCircle, Edit2, Trash2, ChevronDown, ChevronUp } from 'lucide-react';
import { conversationAPI, chatAPI, type Conversation, type Message, type ToolCallHistoryItem } from '../services/api';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeHighlight from 'rehype-highlight';
import rehypeRaw from 'rehype-raw';
import 'highlight.js/styles/github-dark.css';

interface ChatProps {
  onLogout: () => void;
}

const AGENT_TYPES = [
  { value: 'Metaphysics', label: '✨ 玄学命理', icon: '🔮', color: 'from-purple-400 to-pink-400' },
  { value: 'Stock', label: '📈 股票顾问', icon: '💰', color: 'from-green-400 to-blue-400' },
  { value: 'Health', label: '💊 健康助手', icon: '🌿', color: 'from-teal-400 to-cyan-400' },
];

// 历史工具调用显示组件
function HistoricalToolCallDisplay({ toolHistory }: { toolHistory: ToolCallHistoryItem[] }) {
  const [showDetails, setShowDetails] = useState(false);

  return (
    <div className="bg-purple-50 rounded-lg border border-purple-200 overflow-hidden mt-2">
      <div
        className="flex items-center justify-between gap-2 p-3 md:p-2 cursor-pointer hover:bg-purple-100 transition-colors"
        onClick={() => setShowDetails(!showDetails)}
      >
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <span className="text-sm md:text-sm text-purple-700 font-medium">
            工具调用记录 ({toolHistory.length}条)
          </span>
        </div>
        <button className="p-1 rounded hover:bg-purple-200 transition-colors flex-shrink-0">
          {showDetails ? (
            <ChevronUp className="w-5 h-5 md:w-4 md:h-4 text-purple-600" />
          ) : (
            <ChevronDown className="w-5 h-5 md:w-4 md:h-4 text-purple-600" />
          )}
        </button>
      </div>

      {showDetails && (
        <div className="border-t border-purple-200 p-3 md:p-2 bg-purple-25 max-h-60 overflow-y-auto">
          <div className="text-sm md:text-xs text-purple-600 font-semibold mb-2">执行历史：</div>
          {toolHistory.map((item, idx) => (
            <div key={idx} className="text-sm md:text-xs text-purple-700 py-1.5 md:py-1 flex flex-col md:flex-row md:items-start gap-1 md:gap-2">
              <span className="text-purple-400 text-xs md:text-xs flex-shrink-0">
                {new Date(item.Timestamp).toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
              </span>
              <span className="flex-1 break-words">{item.Message}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export default function Chat({ onLogout }: ChatProps) {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [currentConversation, setCurrentConversation] = useState<number | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [inputMessage, setInputMessage] = useState('');
  const [loading, setLoading] = useState(false);
  const [showNewChat, setShowNewChat] = useState(false);
  const [showSidebar, setShowSidebar] = useState(false);
  const [editingMessageId, setEditingMessageId] = useState<number | null>(null);
  const [editingContent, setEditingContent] = useState('');
  const [streamingMessage, setStreamingMessage] = useState(''); // 流式消息累积
  const [toolCallStatus, setToolCallStatus] = useState(''); // 当前工具调用状态
  const [toolCallHistory, setToolCallHistory] = useState<Array<{type: string, message: string, timestamp: number}>>([]);  // 工具调用历史
  const [showToolDetails, setShowToolDetails] = useState(false); // 是否展开工具调用详情
  const [pendingAgentType, setPendingAgentType] = useState<string | null>(null); // 待创建会话的Agent类型
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const abortControllerRef = useRef<AbortController | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const editTextareaRef = useRef<HTMLTextAreaElement>(null);
  const username = localStorage.getItem('username') || 'Guest';

  // 微信浏览器检测和流式消息节流
  const isWeChat = /MicroMessenger/i.test(navigator.userAgent);
  const streamingUpdateTimer = useRef<number | null>(null);
  const pendingStreamingMessage = useRef<string>('');

  useEffect(() => {
    loadConversations();
  }, []);

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  // 当进入编辑模式时,自动调整编辑框高度
  useEffect(() => {
    if (editingMessageId !== null) {
      setTimeout(adjustEditTextareaHeight, 0);
    }
  }, [editingMessageId, editingContent]);

  // 微信浏览器兼容性：监听消息变化，强制刷新渲染
  useEffect(() => {
    // 检测是否为微信浏览器
    const isWeChat = /MicroMessenger/i.test(navigator.userAgent);
    if (isWeChat && messages.length > 0) {
      // 微信浏览器渲染优化：延迟滚动，确保DOM更新完成
      setTimeout(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
      }, 150);
    }
  }, [messages]);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  // 节流更新流式消息（微信浏览器优化）
  const throttledUpdateStreamingMessage = (message: string) => {
    pendingStreamingMessage.current = message;

    // 清除旧的定时器
    if (streamingUpdateTimer.current) {
      return; // 如果已有定时器，只更新pending message，不创建新定时器
    }

    // 微信浏览器：100ms更新一次；其他浏览器：50ms更新一次
    const updateInterval = isWeChat ? 100 : 50;

    streamingUpdateTimer.current = setTimeout(() => {
      setStreamingMessage(pendingStreamingMessage.current);
      streamingUpdateTimer.current = null;
    }, updateInterval);
  };

  // 强制更新流式消息（用于流结束时）
  const forceUpdateStreamingMessage = () => {
    if (streamingUpdateTimer.current) {
      clearTimeout(streamingUpdateTimer.current);
      streamingUpdateTimer.current = null;
    }
    if (pendingStreamingMessage.current) {
      setStreamingMessage(pendingStreamingMessage.current);
    }
  };

  const loadConversations = async () => {
    try {
      // 一次性加载所有会话（设置pageSize为100，最大允许值）
      const res = await conversationAPI.list(1, 100);
      setConversations(res.data.Data.List);
      if (res.data.Data.List.length > 0 && !currentConversation) {
        selectConversation(res.data.Data.List[0].Id);
      }
    } catch (err) {
      console.error('Failed to load conversations', err);
    }
  };

  const selectConversation = async (id: number) => {
    setCurrentConversation(id);
    try {
      const res = await conversationAPI.getDetail(id);
      setMessages(res.data.Data.Messages || []);
    } catch (err) {
      console.error('Failed to load messages', err);
    }
  };

  const createNewConversation = (agentType: string) => {
    // 不立即创建会话，只设置临时状态
    // 会话将在首次发送消息时由后端自动创建
    setCurrentConversation(0); // 使用0表示待创建的会话
    setPendingAgentType(agentType);
    setMessages([]);
    setShowNewChat(false);
  };

  const sendMessage = async (customMessage?: string) => {
    const messageToSend = customMessage || inputMessage;
    if (!messageToSend.trim() || currentConversation === null || loading) return;

    const userMsg: Message = {
      Id: Date.now(),
      Role: 'User',
      Content: messageToSend,
      CreatedAt: new Date().toISOString(),
    };

    setMessages((prev) => [...prev, userMsg]);
    const userInput = messageToSend;
    setInputMessage('');
    setLoading(true);
    setStreamingMessage('');
    setToolCallStatus('');
    setToolCallHistory([]); // 清空工具调用历史
    setShowToolDetails(false); // 收起详情

    // 创建 AbortController 用于取消请求
    const controller = new AbortController();
    abortControllerRef.current = controller;

    try {
      const token = localStorage.getItem('token');
      // 使用相对路径，避免跨域问题
      const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

      // 构建请求体
      const requestBody: any = {
        conversationId: currentConversation === 0 ? null : currentConversation,
        message: userInput,
      };

      // 如果是待创建的会话，添加agentType
      if (currentConversation === 0 && pendingAgentType) {
        requestBody.agentType = pendingAgentType;
      }

      // 使用流式端点
      const response = await fetch(`${API_BASE_URL}/api/chat/agent/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`,
        },
        body: JSON.stringify(requestBody),
        signal: controller.signal,
      });

      if (!response.ok) {
        throw new Error(`HTTP错误: ${response.status}`);
      }

      const reader = response.body?.getReader();
      const decoder = new TextDecoder();
      let accumulatedMessage = '';
      let receivedDone = false; // 跟踪是否收到done事件

      if (!reader) {
        throw new Error('无法获取响应流');
      }

      while (true) {
        const { value, done } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        const lines = chunk.split('\n');

        for (let i = 0; i < lines.length; i++) {
          const line = lines[i].trim();
          if (!line) continue;

          // 解析 SSE 事件
          if (line.startsWith('event:')) {
            const eventType = line.slice(6).trim();
            // 下一行是 data:
            const dataLine = lines[i + 1]?.trim();
            if (dataLine && dataLine.startsWith('data:')) {
              try {
                const data = JSON.parse(dataLine.slice(5).trim());

                switch (eventType) {
                  case 'status':
                    if (data.type === 'start') {
                      const statusMsg = '🚀 ' + data.message;
                      setToolCallStatus(statusMsg);
                      setToolCallHistory(prev => [...prev, { type: 'status', message: statusMsg, timestamp: Date.now() }]);
                    }
                    break;

                  case 'tool_call':
                    const toolMsg = '🔧 ' + (data.message || '正在调用工具...');
                    setToolCallStatus(toolMsg);
                    setToolCallHistory(prev => [...prev, { type: 'tool_call', message: toolMsg, timestamp: Date.now() }]);
                    break;

                  case 'tool_call_start':
                    const toolName = data.toolName || '未知工具';
                    const startMsg = `🔍 正在调用: ${toolName}`;
                    setToolCallStatus(startMsg);
                    setToolCallHistory(prev => [...prev, { type: 'tool_start', message: startMsg, timestamp: Date.now() }]);
                    break;

                  case 'tool_call_end':
                    const resultPreview = data.result?.substring(0, 50) || '';
                    const endMsg = `✅ 完成: ${data.toolName} ${resultPreview ? '- ' + resultPreview + '...' : ''}`;
                    setToolCallStatus(endMsg);
                    setToolCallHistory(prev => [...prev, { type: 'tool_end', message: endMsg, timestamp: Date.now() }]);
                    break;

                  case 'content':
                    if (data.delta) {
                      accumulatedMessage += data.delta;
                      // 使用节流更新，避免微信浏览器渲染问题
                      throttledUpdateStreamingMessage(accumulatedMessage);
                    }
                    break;

                  case 'conversation_created':
                    // 会话已创建，立即更新conversationId并刷新列表
                    if (currentConversation === 0 && data.conversationId) {
                      setCurrentConversation(data.conversationId);
                      setPendingAgentType(null);
                      // 立即刷新会话列表，让用户看到新会话
                      loadConversations();
                    }
                    break;

                  case 'done':
                    receivedDone = true; // 标记已收到done事件
                    setToolCallStatus('');
                    break;

                  case 'error':
                    console.error('流式错误:', data.message);
                    alert('发生错误: ' + data.message);
                    break;
                }
              } catch (err) {
                console.error('解析SSE数据失败:', err);
              }
              i++; // 跳过已处理的 data 行
            }
          }
        }
      }

      // 流式完成，强制更新最后的内容并保存到历史
      forceUpdateStreamingMessage(); // 确保最后的内容被显示

      // 只有在收到done事件后才保存消息到历史（修复Safari提前显示问题）
      if (receivedDone && accumulatedMessage) {
        const aiMsg: Message = {
          Id: Date.now() + 1,
          Role: 'Assistant',
          Content: accumulatedMessage,
          CreatedAt: new Date().toISOString(),
          // 附加工具调用历史（转换格式）
          ToolCallHistory: toolCallHistory.length > 0 ? toolCallHistory.map(item => ({
            Timestamp: new Date(item.timestamp).toISOString(),
            Message: item.message
          })) : undefined,
        };

        // 先添加消息到历史
        setMessages((prev) => [...prev, aiMsg]);

        // 微信浏览器兼容性修复：延迟清除流式状态，确保消息已经渲染
        // 这避免了微信浏览器渲染时序问题
        setTimeout(() => {
          setStreamingMessage('');
          setToolCallStatus('');
          setToolCallHistory([]); // 清空工具调用历史
          pendingStreamingMessage.current = ''; // 清空待更新消息
          // 强制滚动到底部，确保新消息可见
          messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
        }, isWeChat ? 150 : 100);
      } else {
        // 如果没有收到done事件，说明流异常结束，清除状态但不保存
        setStreamingMessage('');
        setToolCallStatus('');
        setToolCallHistory([]); // 清空工具调用历史
        pendingStreamingMessage.current = '';
        if (!receivedDone) {
          console.warn('流在收到done事件前结束，消息未保存');
        }
      }
    } catch (err: any) {
      if (err.name === 'AbortError') {
        console.log('请求已取消');
        // 用户主动停止，清除所有状态
        setStreamingMessage('');
        setToolCallStatus('');
        setToolCallHistory([]);
      } else {
        console.error('发送消息失败:', err);
        alert('发送消息失败: ' + err.message);
      }
    } finally {
      // 清理定时器和状态
      forceUpdateStreamingMessage();
      if (streamingUpdateTimer.current) {
        clearTimeout(streamingUpdateTimer.current);
        streamingUpdateTimer.current = null;
      }
      setLoading(false);
      abortControllerRef.current = null;
    }
  };

  // 停止生成
  const stopGeneration = () => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      // 清理定时器
      if (streamingUpdateTimer.current) {
        clearTimeout(streamingUpdateTimer.current);
        streamingUpdateTimer.current = null;
      }
      forceUpdateStreamingMessage();
      setLoading(false);
    }
  };

  // 编辑消息
  const startEditMessage = (msg: Message) => {
    setEditingMessageId(msg.Id);
    setEditingContent(msg.Content);
  };

  // 取消编辑
  const cancelEdit = () => {
    setEditingMessageId(null);
    setEditingContent('');
  };

  // 保存编辑并重新发送
  const saveAndResend = async () => {
    if (!editingContent.trim() || !currentConversation || editingMessageId === null || loading) return;

    // 找到要编辑的消息在数组中的索引
    const messageIndex = messages.findIndex(m => m.Id === editingMessageId);
    if (messageIndex === -1) {
      console.error('未找到要编辑的消息');
      return;
    }

    // 保存编辑后的内容
    const editedContent = editingContent.trim();

    // 保存原始消息列表以便出错时恢复
    const originalMessages = [...messages];

    try {
      // 删除该消息及其之后的所有消息
      const updatedMessages = messages.slice(0, messageIndex);
      setMessages(updatedMessages);

      // 清除编辑状态
      setEditingMessageId(null);
      setEditingContent('');

      // 直接发送编辑后的消息
      await sendMessage(editedContent);
    } catch (error) {
      console.error('编辑并重新发送失败', error);
      // 如果发送失败，恢复原始消息列表
      setMessages(originalMessages);
      // 恢复编辑状态
      setEditingMessageId(editingMessageId);
      setEditingContent(editedContent);
      alert('发送失败，请重试');
    }
  };

  const clearChatHistory = async () => {
    if (!currentConversation) return;

    const confirmClear = window.confirm('确定要清除当前对话的历史记录吗？清除后将无法恢复。');
    if (!confirmClear) return;

    try {
      await chatAPI.clearHistory(currentConversation);
      // 清除本地显示的消息
      setMessages([]);
      alert('聊天历史已清除！');
    } catch (err) {
      console.error('Failed to clear chat history', err);
      alert('清除聊天历史失败，请稍后重试。');
    }
  };

  // 删除会话
  const deleteConversation = async (id: number, e: React.MouseEvent) => {
    e.stopPropagation(); // 阻止事件冒泡，避免触发选中会话

    const confirmDelete = window.confirm('确定要删除这个会话吗？删除后将无法恢复！');
    if (!confirmDelete) return;

    try {
      await conversationAPI.delete(id);

      // 如果删除的是当前选中的会话，清空当前会话
      if (currentConversation === id) {
        setCurrentConversation(null);
        setMessages([]);
      }

      // 重新加载会话列表
      await loadConversations();

      alert('会话已删除！');
    } catch (err) {
      console.error('Failed to delete conversation', err);
      alert('删除会话失败，请稍后重试。');
    }
  };

  const getCurrentAgent = () => {
    const conv = conversations.find((c) => c.Id === currentConversation);
    return AGENT_TYPES.find((a) => a.value === conv?.AgentType) || AGENT_TYPES[0];
  };

  // 格式化时间显示
  const formatTime = (dateString: string) => {
    const date = new Date(dateString);
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    const minutes = Math.floor(diff / 60000);
    const hours = Math.floor(diff / 3600000);
    const days = Math.floor(diff / 86400000);

    if (minutes < 1) return '刚刚';
    if (minutes < 60) return `${minutes}分钟前`;
    if (hours < 24) return `${hours}小时前`;
    if (days < 7) return `${days}天前`;

    return date.toLocaleString('zh-CN', {
      month: 'numeric',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  // 自动调整主输入框高度
  const adjustTextareaHeight = () => {
    const textarea = textareaRef.current;
    if (textarea) {
      textarea.style.height = 'auto';
      textarea.style.height = `${Math.min(textarea.scrollHeight, 200)}px`;
    }
  };

  // 自动调整编辑框高度
  const adjustEditTextareaHeight = () => {
    const textarea = editTextareaRef.current;
    if (textarea) {
      textarea.style.height = 'auto';
      textarea.style.height = `${Math.min(textarea.scrollHeight, 300)}px`;
    }
  };

  // 处理主输入框变化
  const handleInputChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setInputMessage(e.target.value);
    adjustTextareaHeight();
  };

  // 处理编辑框变化
  const handleEditChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    setEditingContent(e.target.value);
    setTimeout(adjustEditTextareaHeight, 0);
  };

  // 处理键盘事件：Enter发送，Shift+Enter换行
  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (!loading && inputMessage.trim()) {
        sendMessage();
      }
    }
  };

  return (
    <div className="flex h-screen bg-gradient-to-br from-pink-50 via-purple-50 to-pink-100">
      {/* Mobile Overlay */}
      {showSidebar && (
        <div
          className="fixed inset-0 bg-black/50 z-40 lg:hidden"
          onClick={() => setShowSidebar(false)}
        />
      )}

      {/* Sidebar */}
      <div className={`fixed lg:relative w-80 h-full bg-white/80 backdrop-blur-sm border-r-2 border-pink-100 flex flex-col z-50 transform transition-transform duration-300 lg:transform-none ${
        showSidebar ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'
      }`}>
        <div className="p-4 border-b-2 border-pink-100">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center gap-2">
              <button
                onClick={() => setShowSidebar(false)}
                className="lg:hidden p-2 hover:bg-pink-50 rounded-full transition-colors"
              >
                <X className="w-5 h-5 text-pink-400" />
              </button>
              <div>
                <h2 className="text-xl font-bold bg-gradient-to-r from-pink-500 to-purple-500 bg-clip-text text-transparent">
                  Hi, {username}! 💕
                </h2>
                <p className="text-sm text-gray-500">选择一个助手开始聊天吧~</p>
              </div>
            </div>
            <button onClick={onLogout} className="p-2 hover:bg-pink-50 rounded-full transition-colors">
              <LogOut className="w-5 h-5 text-pink-400" />
            </button>
          </div>
          <button
            onClick={() => setShowNewChat(!showNewChat)}
            className="cute-button w-full flex items-center justify-center gap-2"
          >
            <Plus className="w-5 h-5" />
            新建对话
          </button>
        </div>

        {showNewChat && (
          <div className="p-4 bg-pink-50 border-b-2 border-pink-100 space-y-2">
            {AGENT_TYPES.map((agent) => (
              <button
                key={agent.value}
                onClick={() => createNewConversation(agent.value)}
                className={`w-full p-3 rounded-2xl bg-gradient-to-r ${agent.color} text-white shadow-md hover:shadow-lg transition-all transform hover:scale-105`}
              >
                <span className="text-xl mr-2">{agent.icon}</span>
                {agent.label}
              </button>
            ))}
          </div>
        )}

        <div className="flex-1 overflow-y-auto p-3 space-y-2">
          {conversations.map((conv) => (
            <div
              key={conv.Id}
              className={`relative w-full rounded-2xl transition-all group ${
                currentConversation === conv.Id
                  ? 'bg-gradient-to-r from-pink-400 to-purple-400 text-white shadow-lg'
                  : 'bg-white/60 hover:bg-white hover:shadow-md'
              }`}
            >
              <button
                onClick={() => selectConversation(conv.Id)}
                className="w-full p-3 pr-12 flex items-center gap-3 text-left"
              >
                <MessageCircle className="w-4 h-4 flex-shrink-0" />
                <div className="flex-1 min-w-0">
                  <div className="font-medium truncate mb-0.5">{conv.Title || '新对话'}</div>
                  <div className={`text-xs ${currentConversation === conv.Id ? 'text-white/80' : 'text-gray-500'}`}>
                    {conv.MessageCount} 条消息
                  </div>
                </div>
              </button>
              <button
                onClick={(e) => deleteConversation(conv.Id, e)}
                className={`absolute right-2 top-1/2 -translate-y-1/2 p-2 rounded-full transition-all ${
                  currentConversation === conv.Id
                    ? 'bg-white/10 hover:bg-white/20 md:opacity-0 md:group-hover:opacity-100'
                    : 'bg-transparent hover:bg-red-50 md:opacity-0 md:group-hover:opacity-100'
                }`}
                title="删除会话"
              >
                <Trash2 className={`w-4 h-4 ${currentConversation === conv.Id ? 'text-white drop-shadow-md' : 'text-red-500'}`} />
              </button>
            </div>
          ))}
        </div>
      </div>

      {/* Chat Area */}
      <div className="flex-1 flex flex-col">
        {currentConversation !== null ? (
          <>
            {/* Header */}
            <div className="bg-white/80 backdrop-blur-sm border-b-2 border-pink-100 p-4">
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-3">
                  <button
                    onClick={() => setShowSidebar(true)}
                    className="lg:hidden p-2 hover:bg-pink-50 rounded-full transition-colors"
                  >
                    <Menu className="w-5 h-5 text-pink-400" />
                  </button>
                  <div className={`p-3 rounded-full bg-gradient-to-r ${getCurrentAgent().color}`}>
                    <span className="text-2xl">{getCurrentAgent().icon}</span>
                  </div>
                  <div>
                    <h3 className="font-bold text-gray-800">{getCurrentAgent().label}</h3>
                    <p className="text-sm text-gray-500">在线中 • 随时为你服务</p>
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={clearChatHistory}
                    className="p-2 hover:bg-pink-50 rounded-full transition-colors flex-shrink-0"
                    title="清除聊天历史"
                  >
                    <RotateCcw className="w-5 h-5 text-pink-400" />
                  </button>
                  <button
                    onClick={onLogout}
                    className="lg:hidden p-2 hover:bg-pink-50 rounded-full transition-colors flex-shrink-0"
                    title="登出"
                  >
                    <LogOut className="w-5 h-5 text-pink-400" />
                  </button>
                </div>
              </div>
            </div>

            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-4 md:p-6 space-y-4">
              {messages.map((msg) => (
                <div
                  key={msg.Id}
                  className={`flex ${msg.Role?.toLowerCase() === 'user' ? 'justify-end' : 'justify-start'}`}
                >
                  <div className={`flex items-start gap-2 w-full ${msg.Role?.toLowerCase() === 'user' ? 'flex-row-reverse justify-end' : 'justify-start'}`}>
                    <div className="flex flex-col gap-1">
                      <div
                        className={msg.Role?.toLowerCase() === 'user' ? 'user-message' : 'ai-message'}
                      >
                        {msg.Role?.toLowerCase() === 'user' ? (
                          editingMessageId === msg.Id ? (
                            <div className="w-full max-w-2xl space-y-3 bg-gradient-to-br from-pink-50 to-purple-50 p-4 md:p-5 rounded-2xl shadow-xl border-2 border-pink-300">
                              <div className="flex items-center gap-2 text-base md:text-sm font-semibold text-pink-600 mb-1">
                                <Edit2 className="w-5 h-5 md:w-4 md:h-4" />
                                <span>编辑消息</span>
                              </div>
                              <textarea
                                ref={editTextareaRef}
                                value={editingContent}
                                onChange={handleEditChange}
                                className="w-full p-4 text-base leading-relaxed bg-white text-gray-800 border-2 border-pink-300 rounded-xl focus:outline-none focus:border-pink-500 focus:ring-4 focus:ring-pink-200 transition-all resize-none shadow-sm"
                                style={{ minHeight: '140px', maxHeight: '400px' }}
                                placeholder="输入修改后的内容..."
                                autoFocus
                              />
                              <div className="flex flex-col sm:flex-row gap-3 sm:gap-2 sm:justify-end">
                                <button
                                  onClick={cancelEdit}
                                  className="w-full sm:w-auto px-6 py-3.5 md:px-5 md:py-2.5 text-base md:text-sm font-medium bg-white hover:bg-gray-50 text-gray-700 border-2 border-gray-300 rounded-xl transition-all shadow-sm hover:shadow active:scale-98"
                                >
                                  <div className="flex items-center justify-center gap-2">
                                    <X className="w-4 h-4" />
                                    <span>取消</span>
                                  </div>
                                </button>
                                <button
                                  onClick={saveAndResend}
                                  disabled={!editingContent.trim()}
                                  className="w-full sm:w-auto px-6 py-3.5 md:px-5 md:py-2.5 text-base md:text-sm font-medium bg-gradient-to-r from-pink-500 to-purple-500 hover:from-pink-600 hover:to-purple-600 disabled:from-gray-300 disabled:to-gray-400 disabled:cursor-not-allowed text-white rounded-xl shadow-lg hover:shadow-xl transition-all active:scale-98"
                                >
                                  <div className="flex items-center justify-center gap-2">
                                    <Send className="w-4 h-4" />
                                    <span>保存并重新发送</span>
                                  </div>
                                </button>
                              </div>
                            </div>
                          ) : (
                            msg.Content
                          )
                        ) : (
                      <div className="flex flex-col gap-3">
                        <ReactMarkdown
                          key={`msg-${msg.Id}-${msg.Content.substring(0, 50)}`}
                          remarkPlugins={[remarkGfm]}
                          rehypePlugins={[rehypeHighlight, rehypeRaw]}
                          components={{
                            code({ inline, className, children, ...props }: any) {
                              return inline ? (
                                <code className="bg-pink-100 text-pink-800 px-1 py-0.5 rounded" {...props}>
                                  {children}
                                </code>
                              ) : (
                                <code className={className} {...props}>
                                  {children}
                                </code>
                              );
                            },
                            pre({ children, ...props }: any) {
                              return (
                                <pre className="bg-gray-900 text-gray-100 rounded-lg p-4 overflow-x-auto my-2" {...props}>
                                  {children}
                                </pre>
                              );
                            },
                            a({ href, children, ...props }: any) {
                              return (
                                <a href={href} className="text-pink-500 hover:text-pink-600 underline" target="_blank" rel="noopener noreferrer" {...props}>
                                  {children}
                                </a>
                              );
                            },
                            table({ children, ...props }: any) {
                              return (
                                <div className="overflow-x-auto my-4">
                                  <table className="min-w-full border border-pink-200 rounded-lg" {...props}>
                                    {children}
                                  </table>
                                </div>
                              );
                            },
                            th({ children, ...props }: any) {
                              return (
                                <th className="border border-pink-200 bg-pink-50 px-4 py-2 font-semibold" {...props}>
                                  {children}
                                </th>
                              );
                            },
                            td({ children, ...props }: any) {
                              return (
                                <td className="border border-pink-200 px-4 py-2" {...props}>
                                  {children}
                                </td>
                              );
                            },
                            ul({ children, ...props }: any) {
                              return <ul className="list-disc list-inside my-2 space-y-1" {...props}>{children}</ul>;
                            },
                            ol({ children, ...props }: any) {
                              return <ol className="list-decimal list-inside my-2 space-y-1" {...props}>{children}</ol>;
                            },
                            blockquote({ children, ...props }: any) {
                              return (
                                <blockquote className="border-l-4 border-pink-300 pl-4 py-2 my-2 bg-pink-50 rounded-r" {...props}>
                                  {children}
                                </blockquote>
                              );
                            },
                            h1({ children, ...props }: any) {
                              return <h1 className="text-2xl font-bold my-3" {...props}>{children}</h1>;
                            },
                            h2({ children, ...props }: any) {
                              return <h2 className="text-xl font-bold my-2" {...props}>{children}</h2>;
                            },
                            h3({ children, ...props }: any) {
                              return <h3 className="text-lg font-bold my-2" {...props}>{children}</h3>;
                            },
                            p({ children, ...props }: any) {
                              return <p className="my-2" {...props}>{children}</p>;
                            },
                          }}
                        >
                          {msg.Content}
                        </ReactMarkdown>

                        {/* 显示历史消息的工具调用记录 */}
                        {msg.ToolCallHistory && msg.ToolCallHistory.length > 0 && (
                          <HistoricalToolCallDisplay toolHistory={msg.ToolCallHistory} />
                        )}
                      </div>
                      )}
                      </div>
                      <div className={`text-xs text-gray-400 px-2 ${msg.Role?.toLowerCase() === 'user' ? 'text-right' : 'text-left'}`}>
                        {formatTime(msg.CreatedAt)}
                      </div>
                    </div>
                    {msg.Role?.toLowerCase() === 'user' && editingMessageId !== msg.Id && !loading && (
                      <button
                        onClick={() => startEditMessage(msg)}
                        className="p-3 md:p-2 hover:bg-pink-50 rounded-full transition-colors flex-shrink-0"
                        title="编辑并重新发送"
                      >
                        <Edit2 className="w-5 h-5 md:w-4 md:h-4 text-pink-400" />
                      </button>
                    )}
                  </div>
                </div>
              ))}
              {loading && (
                <div className="flex justify-start">
                  <div className="ai-message flex flex-col gap-3 w-full max-w-full md:min-w-[320px]">
                    {/* 工具调用状态 */}
                    {(toolCallStatus || toolCallHistory.length > 0) && (
                      <div className="bg-purple-50 rounded-lg border border-purple-200 overflow-hidden">
                        {/* 简化显示 + 展开/收起按钮 */}
                        <div
                          className="flex items-center justify-between gap-2 p-3 md:p-2 cursor-pointer hover:bg-purple-100 transition-colors active:bg-purple-100"
                          onClick={() => setShowToolDetails(!showToolDetails)}
                        >
                          <div className="flex items-center gap-2 flex-1 min-w-0">
                            <div className="flex gap-1 flex-shrink-0">
                              <div className="w-2 h-2 bg-purple-400 rounded-full animate-bounce"></div>
                              <div className="w-2 h-2 bg-pink-400 rounded-full animate-bounce" style={{ animationDelay: '0.1s' }}></div>
                            </div>
                            <span className="text-sm md:text-sm text-purple-700 font-medium truncate">
                              {toolCallStatus || '正在处理...'}
                            </span>
                          </div>
                          <button className="p-1 rounded hover:bg-purple-200 transition-colors flex-shrink-0">
                            {showToolDetails ? (
                              <ChevronUp className="w-5 h-5 md:w-4 md:h-4 text-purple-600" />
                            ) : (
                              <ChevronDown className="w-5 h-5 md:w-4 md:h-4 text-purple-600" />
                            )}
                          </button>
                        </div>

                        {/* 详细工具调用历史 */}
                        {showToolDetails && toolCallHistory.length > 0 && (
                          <div className="border-t border-purple-200 p-3 md:p-2 bg-purple-25 max-h-60 overflow-y-auto">
                            <div className="text-sm md:text-xs text-purple-600 font-semibold mb-2">执行历史：</div>
                            {toolCallHistory.map((item, idx) => (
                              <div key={idx} className="text-sm md:text-xs text-purple-700 py-1.5 md:py-1 flex flex-col md:flex-row md:items-start gap-1 md:gap-2">
                                <span className="text-purple-400 text-xs md:text-xs flex-shrink-0">{new Date(item.timestamp).toLocaleTimeString('zh-CN', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}</span>
                                <span className="flex-1 break-words">{item.message}</span>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    )}

                    {/* 流式消息显示 */}
                    {streamingMessage ? (
                      <div className="prose prose-sm md:prose-base max-w-none break-words overflow-wrap-anywhere">
                        <ReactMarkdown
                          key={`streaming-${streamingMessage.substring(0, 50)}`}
                          remarkPlugins={[remarkGfm]}
                          rehypePlugins={[rehypeHighlight, rehypeRaw]}
                          components={{
                            p: ({children}) => <p className="break-words whitespace-pre-wrap">{children}</p>,
                            code: ({children}) => <code className="break-words text-xs md:text-sm">{children}</code>,
                            pre: ({children}) => <pre className="overflow-x-auto text-xs md:text-sm">{children}</pre>
                          }}
                        >
                          {streamingMessage}
                        </ReactMarkdown>
                      </div>
                    ) : (
                      <div className="flex items-center gap-2">
                        <div className="flex gap-1">
                          <div className="w-2 h-2 bg-pink-400 rounded-full animate-bounce"></div>
                          <div className="w-2 h-2 bg-purple-400 rounded-full animate-bounce" style={{ animationDelay: '0.1s' }}></div>
                          <div className="w-2 h-2 bg-pink-400 rounded-full animate-bounce" style={{ animationDelay: '0.2s' }}></div>
                        </div>
                        <span className="text-sm text-gray-500">正在处理...</span>
                      </div>
                    )}

                    {/* 光标闪烁效果 */}
                    {streamingMessage && (
                      <span className="inline-block w-2 h-4 bg-pink-400 animate-pulse ml-1"></span>
                    )}
                  </div>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Input */}
            <div className="bg-white/80 backdrop-blur-sm border-t-2 border-pink-100 p-3 md:p-4">
              <div className="flex gap-2 items-end">
                <textarea
                  ref={textareaRef}
                  value={inputMessage}
                  onChange={handleInputChange}
                  onKeyDown={handleKeyDown}
                  placeholder="输入你的问题~ 💭（Enter发送，Shift+Enter换行）"
                  className="cute-input flex-1 text-base leading-relaxed resize-none overflow-y-auto px-4 py-3"
                  style={{ minHeight: '48px', maxHeight: '200px' }}
                  rows={1}
                  disabled={loading}
                />
                {loading ? (
                  <button
                    onClick={stopGeneration}
                    className="cute-button px-5 py-3 md:px-6 md:py-2 bg-red-400 hover:bg-red-500"
                    title="停止生成"
                  >
                    <StopCircle className="w-5 h-5 md:w-5 md:h-5" />
                  </button>
                ) : (
                  <button
                    onClick={() => sendMessage()}
                    disabled={!inputMessage.trim()}
                    className="cute-button px-5 py-3 md:px-6 md:py-2 disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <Send className="w-5 h-5 md:w-5 md:h-5" />
                  </button>
                )}
              </div>
            </div>
          </>
        ) : (
          <div className="flex-1 flex flex-col">
            {/* Empty State Header - Always visible on small screens */}
            <div className="bg-white/80 backdrop-blur-sm border-b-2 border-pink-100 p-4">
              <div className="flex items-center justify-between gap-3">
                <button
                  onClick={() => setShowSidebar(true)}
                  className="lg:hidden p-2 hover:bg-pink-50 rounded-full transition-colors"
                >
                  <Menu className="w-5 h-5 text-pink-400" />
                </button>
                <h2 className="text-lg font-bold bg-gradient-to-r from-pink-500 to-purple-500 bg-clip-text text-transparent">
                  AgentHub 💕
                </h2>
                <button
                  onClick={onLogout}
                  className="p-2 hover:bg-pink-50 rounded-full transition-colors flex-shrink-0"
                  title="登出"
                >
                  <LogOut className="w-5 h-5 text-pink-400" />
                </button>
              </div>
            </div>
            {/* Empty State Content */}
            <div className="flex-1 flex items-center justify-center">
              <div className="text-center space-y-4">
                <Sparkles className="w-20 h-20 text-pink-300 mx-auto animate-bounce-slow" />
                <h3 className="text-2xl font-bold text-gray-600">选择或创建一个对话吧~</h3>
                <p className="text-gray-400">点击左侧的"新建对话"开始和AI聊天 ✨</p>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
