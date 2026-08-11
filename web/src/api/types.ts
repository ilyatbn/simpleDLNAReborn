// Hand-mirrored from admin/Api/Dtos.cs. There is no OpenAPI document; writing
// one to generate a dozen interfaces would cost more than it saves.

export type ServerState =
  | 'idle'
  | 'loading'
  | 'running'
  | 'refreshing'
  | 'stopped'

export interface FieldError {
  field: string
  message: string
}

export interface ErrorBody {
  code: string
  message: string
  details?: FieldError[]
}

export interface Playback {
  playing: boolean
  title?: string
  client?: string
  mediaType?: string
  startedUtc?: string
}

export interface Status {
  version: string
  signature: string
  mediaPort: number
  adminPort: number
  startedUtc: string
  cacheDir: string
  configDir: string
  browseUrl: string
  host: 'tray' | 'console'
  /** False when servers come from the command line; mutations answer 409. */
  managed: boolean
  playback?: Playback | null
  serverCount: { total: number; running: number }
}

export interface ViewParameter {
  name: string
  type: string
  unit?: string
  default?: string
  description?: string
}

export interface NamedItem {
  name: string
  description: string
  default?: boolean
  configurable?: boolean
  parameters?: ViewParameter[]
}

export interface Capabilities {
  orders: NamedItem[]
  views: NamedItem[]
  mediaTypes: string[]
  restrictionTypes: string[]
  logLevels: string[]
}

export interface Restrictions {
  mac: string[]
  ip: string[]
  userAgent: string[]
}

export interface Server {
  id: string
  name: string
  active: boolean
  state: ServerState
  lastError?: string | null
  order: string
  orderDescending: boolean
  types: string[]
  views: string[]
  directories: string[]
  restrictions: Restrictions
  uuid?: string | null
  mountPrefix?: string | null
  startedUtc?: string | null
  loadSeconds?: number | null
}

export interface ServerInput {
  name: string
  order: string
  orderDescending: boolean
  types: string[]
  views: string[]
  directories: string[]
  restrictions: Restrictions
}

export interface Settings {
  port: number
  cacheDir: string
  rescanDelaySeconds: number
  rescanIntervalMinutes: number
  logLevel: string
  startMinimized?: boolean | null
  preventSleep: boolean
  autostart?: boolean | null
  effective: { port: number; cacheDir: string }
  restartRequired: string[]
}

export interface LogLine {
  timestamp?: string
  level?: string
  logger?: string
  message: string
}

export interface LogTail {
  path?: string
  level?: string
  disabled: boolean
  totalBytes: number
  lines: LogLine[]
}

export interface FsEntry {
  name: string
  path: string
  hasChildren: boolean
  accessible: boolean
}

export interface FsListing {
  path?: string | null
  parent?: string | null
  entries: FsEntry[]
}
