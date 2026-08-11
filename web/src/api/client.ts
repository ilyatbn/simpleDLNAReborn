import type {
  Capabilities,
  ErrorBody,
  FsListing,
  LogTail,
  Server,
  ServerInput,
  Settings,
  Status,
} from './types'

const BASE = '/api/v1'

/** A structured API failure, carrying the server's per-field messages. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly details: { field: string; message: string }[]

  constructor(status: number, body: ErrorBody | null, fallback: string) {
    super(body?.message ?? fallback)
    this.status = status
    this.code = body?.code ?? 'unknown'
    this.details = body?.details ?? []
  }
}

/** Raised when the backend cannot be reached at all - the process exited. */
export class OfflineError extends Error {
  constructor() {
    super('SimpleDLNA is not running')
  }
}

type Init = Omit<RequestInit, 'body'> & { body?: unknown }

async function request<T>(path: string, init?: Init): Promise<T> {
  const headers: Record<string, string> = {}
  let body: BodyInit | undefined
  if (init?.body !== undefined) {
    // Required by the API: it makes cross-origin posts non-simple, so the
    // browser preflights them and the preflight fails.
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(init.body)
  }

  let response: Response
  try {
    response = await fetch(BASE + path, { ...init, headers, body })
  } catch {
    throw new OfflineError()
  }

  if (response.status === 204) {
    return undefined as T
  }

  const text = await response.text()
  const parsed = text ? safeParse(text) : null

  if (!response.ok) {
    throw new ApiError(
      response.status,
      (parsed as { error?: ErrorBody } | null)?.error ?? null,
      `Request failed with status ${response.status}`,
    )
  }
  return parsed as T
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return null
  }
}

export const api = {
  status: () => request<Status>('/status'),
  capabilities: () => request<Capabilities>('/capabilities'),

  servers: () =>
    request<{ servers: Server[] }>('/servers').then((r) => r.servers),
  server: (id: string) => request<Server>(`/servers/${id}`),
  createServer: (input: ServerInput) =>
    request<Server>('/servers', { method: 'POST', body: input }),
  updateServer: (id: string, input: ServerInput) =>
    request<Server>(`/servers/${id}`, { method: 'PUT', body: input }),
  deleteServer: (id: string) =>
    request<void>(`/servers/${id}`, { method: 'DELETE' }),
  startServer: (id: string) =>
    request<Server>(`/servers/${id}/start`, { method: 'POST' }),
  stopServer: (id: string) =>
    request<Server>(`/servers/${id}/stop`, { method: 'POST' }),
  rescanServer: (id: string) =>
    request<Server>(`/servers/${id}/rescan`, { method: 'POST' }),
  rescanAll: () =>
    request<{ requested: number; skipped: number }>('/servers/rescan-all', {
      method: 'POST',
    }),

  settings: () => request<Settings>('/settings'),
  saveSettings: (input: Partial<Settings>) =>
    request<Settings>('/settings', { method: 'PUT', body: input }),

  dropCache: () => request<void>('/cache/drop', { method: 'POST' }),
  log: (tail: number, level?: string) =>
    request<LogTail>(
      `/log?tail=${tail}${level ? `&level=${encodeURIComponent(level)}` : ''}`,
    ),
  browse: (path?: string | null) =>
    request<FsListing>(
      path ? `/fs?path=${encodeURIComponent(path)}` : '/fs',
    ),
}
