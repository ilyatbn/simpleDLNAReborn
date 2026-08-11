import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import type { ServerState } from '../api/types'

/* ---------- state badge ---------- */

export function StateBadge({
  state,
  error,
}: {
  state: ServerState
  error?: string | null
}) {
  // Colour is never the only signal - the state name is always spelled out.
  const cls = error && state === 'stopped' ? 'error' : state
  return (
    <span className={`badge ${cls}`} title={error ?? undefined}>
      <span className="dot" aria-hidden="true" />
      {error && state === 'stopped' ? 'failed' : state}
    </span>
  )
}

/* ---------- modal ---------- */

export function Modal({
  title,
  children,
  footer,
  onClose,
}: {
  title: string
  children: ReactNode
  footer?: ReactNode
  onClose: () => void
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div
      className="backdrop"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose()
      }}
    >
      <div className="modal" role="dialog" aria-modal="true" aria-label={title}>
        <header>{title}</header>
        <div className="body">{children}</div>
        {footer && <footer>{footer}</footer>}
      </div>
    </div>
  )
}

export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'OK',
  danger,
  onConfirm,
  onCancel,
}: {
  title: string
  message: string
  confirmLabel?: string
  danger?: boolean
  onConfirm: () => void
  onCancel: () => void
}) {
  return (
    <Modal
      title={title}
      onClose={onCancel}
      footer={
        <>
          <button onClick={onCancel}>Cancel</button>
          <button
            className={danger ? 'danger' : 'primary'}
            onClick={onConfirm}
            autoFocus
          >
            {confirmLabel}
          </button>
        </>
      }
    >
      <p style={{ margin: 0 }}>{message}</p>
    </Modal>
  )
}

/* ---------- toasts ---------- */

interface Toast {
  id: number
  message: string
  error?: boolean
}

interface ToastApi {
  push: (message: string, error?: boolean) => void
}

const ToastContext = createContext<ToastApi>({ push: () => undefined })

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const push = useCallback((message: string, error?: boolean) => {
    const id = Date.now() + Math.random()
    setToasts((t) => [...t, { id, message, error }])
    window.setTimeout(
      () => setToasts((t) => t.filter((x) => x.id !== id)),
      error ? 8000 : 4000,
    )
  }, [])

  const value = useMemo(() => ({ push }), [push])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toasts" aria-live="polite" aria-atomic="false">
        {toasts.map((t) => (
          <div key={t.id} className={`toast${t.error ? ' error' : ''}`}>
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast() {
  return useContext(ToastContext)
}

/* ---------- misc ---------- */

export function Spinner({ label = 'Loading…' }: { label?: string }) {
  return (
    <p className="muted" role="status">
      {label}
    </p>
  )
}

export function ErrorState({ error }: { error: Error }) {
  return (
    <div className="banner offline" role="alert">
      {error.message}
    </div>
  )
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let i = 0
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024
    i++
  }
  return `${value.toFixed(1)} ${units[i]}`
}
