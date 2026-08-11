import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { api, OfflineError } from './client'
import type { Server, Status } from './types'

interface Live {
  status: Status | undefined
  servers: Server[] | undefined
  /** True once a request has failed to connect - the process is gone. */
  offline: boolean
  refreshServers: () => void
  refreshStatus: () => void
}

const LiveContext = createContext<Live | null>(null)

const TRANSITIONAL = new Set(['loading', 'refreshing'])

export function LiveProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>()
  const [servers, setServers] = useState<Server[]>()
  const [offline, setOffline] = useState(false)
  const sseAlive = useRef(false)

  const refreshStatus = useCallback(() => {
    api
      .status()
      .then((rv) => {
        setStatus(rv)
        setOffline(false)
      })
      .catch((ex) => {
        if (ex instanceof OfflineError) setOffline(true)
      })
  }, [])

  const refreshServers = useCallback(() => {
    api
      .servers()
      .then((rv) => {
        setServers(rv)
        setOffline(false)
      })
      .catch((ex) => {
        if (ex instanceof OfflineError) setOffline(true)
      })
  }, [])

  useEffect(() => {
    refreshStatus()
    refreshServers()
  }, [refreshStatus, refreshServers])

  // Server-Sent Events. Every event is a nudge to refetch, so a missed one
  // cannot leave the UI inconsistent.
  useEffect(() => {
    let source: EventSource | undefined
    let retry: number | undefined
    let closed = false

    const connect = () => {
      if (closed) return
      source = new EventSource('/api/v1/events')
      source.addEventListener('open', () => {
        sseAlive.current = true
        setOffline(false)
      })
      source.addEventListener('servers', () => {
        refreshServers()
        refreshStatus()
      })
      source.addEventListener('playback', () => refreshStatus())
      source.addEventListener('ping', () => setOffline(false))
      source.addEventListener('error', () => {
        sseAlive.current = false
        source?.close()
        // The stream drops whenever the process exits; keep trying so the UI
        // recovers by itself when it comes back.
        retry = window.setTimeout(connect, 3000)
      })
    }
    connect()

    return () => {
      closed = true
      if (retry) window.clearTimeout(retry)
      source?.close()
    }
  }, [refreshServers, refreshStatus])

  // Fallback polling. The app stays fully usable when EventSource never
  // connects; it just refreshes on a timer instead.
  useEffect(() => {
    const tick = () => {
      const busy = (servers ?? []).some((s) => TRANSITIONAL.has(s.state))
      if (!sseAlive.current || busy) {
        refreshServers()
        refreshStatus()
      }
    }
    const busy = (servers ?? []).some((s) => TRANSITIONAL.has(s.state))
    const timer = window.setInterval(tick, busy ? 1000 : 5000)
    return () => window.clearInterval(timer)
  }, [servers, refreshServers, refreshStatus])

  const value = useMemo(
    () => ({ status, servers, offline, refreshServers, refreshStatus }),
    [status, servers, offline, refreshServers, refreshStatus],
  )

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>
}

export function useLive(): Live {
  const ctx = useContext(LiveContext)
  if (!ctx) throw new Error('useLive must be used inside LiveProvider')
  return ctx
}
