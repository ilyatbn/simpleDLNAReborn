import { useEffect, useState } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { useLive } from './api/live'
import { AboutPage } from './features/about/AboutPage'
import { LogsPage } from './features/logs/LogsPage'
import { ServerEditorPage } from './features/servers/ServerEditorPage'
import { ServersPage } from './features/servers/ServersPage'
import { SettingsPage } from './features/settings/SettingsPage'

type Theme = 'system' | 'light' | 'dark'

export function App() {
  const { status, offline } = useLive()
  const [theme, setTheme] = useState<Theme>(
    () => (localStorage.getItem('theme') as Theme) ?? 'system',
  )

  useEffect(() => {
    if (theme === 'system') {
      document.documentElement.removeAttribute('data-theme')
    } else {
      document.documentElement.setAttribute('data-theme', theme)
    }
    localStorage.setItem('theme', theme)
  }, [theme])

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          SimpleDLNA
          {status && <small>port {status.mediaPort}</small>}
        </div>
        <nav className="nav">
          <NavLink to="/" end>
            Servers
          </NavLink>
          <NavLink to="/settings">Settings</NavLink>
          <NavLink to="/logs">Log</NavLink>
          <NavLink to="/about">About</NavLink>
        </nav>
        <div className="topbar-right">
          <span aria-live="polite">
            {status?.playback?.playing
              ? `▶ ${status.playback.title} — ${status.playback.client}`
              : 'Nothing playing'}
          </span>
          {status && (
            <a href={status.browseUrl} target="_blank" rel="noreferrer noopener">
              Browse media
            </a>
          )}
          <button
            className="ghost"
            title="Switch theme"
            aria-label="Switch theme"
            onClick={() =>
              setTheme(
                theme === 'system'
                  ? 'light'
                  : theme === 'light'
                    ? 'dark'
                    : 'system',
              )
            }
          >
            {theme === 'system' ? '🌗' : theme === 'light' ? '☀️' : '🌙'}
          </button>
        </div>
      </header>

      <main className="main">
        {offline && (
          <div className="banner offline" role="alert" style={{ marginBottom: '1rem' }}>
            SimpleDLNA is not running. This page will reconnect automatically
            once it starts again.
          </div>
        )}
        <Routes>
          <Route path="/" element={<ServersPage />} />
          <Route path="/servers/new" element={<ServerEditorPage />} />
          <Route path="/servers/:id" element={<ServerEditorPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/logs" element={<LogsPage />} />
          <Route path="/about" element={<AboutPage />} />
          <Route
            path="*"
            element={
              <div className="empty-state">
                <h2>Not found</h2>
                <p>That page does not exist.</p>
              </div>
            }
          />
        </Routes>
      </main>
    </div>
  )
}
