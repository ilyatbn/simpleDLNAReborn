import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { useAsync } from '../../api/useAsync'
import { ErrorState, Spinner, formatBytes } from '../../components/ui'

const LEVELS = ['', 'Debug', 'Info', 'Warn', 'Error', 'Fatal']

export function LogsPage() {
  const [tail, setTail] = useState(200)
  const [level, setLevel] = useState('')
  const [auto, setAuto] = useState(false)
  const log = useAsync(() => api.log(tail, level || undefined), [tail, level])

  useEffect(() => {
    if (!auto) return
    const timer = window.setInterval(() => log.reload(), 5000)
    return () => window.clearInterval(timer)
  }, [auto, log])

  return (
    <>
      <div className="page-head">
        <h1>Log</h1>
        <div className="spacer" />
        <label className="field">
          Level
          <select value={level} onChange={(e) => setLevel(e.target.value)}>
            {LEVELS.map((l) => (
              <option key={l} value={l}>
                {l === '' ? 'All' : l}
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Lines
          <select
            value={tail}
            onChange={(e) => setTail(Number(e.target.value))}
          >
            {[100, 200, 500, 1000, 5000].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
        <label className="check">
          <input
            type="checkbox"
            checked={auto}
            onChange={(e) => setAuto(e.target.checked)}
          />
          Auto-refresh
        </label>
        <button onClick={() => log.reload()}>Refresh</button>
      </div>

      {log.loading && !log.data && <Spinner />}
      {log.error && <ErrorState error={log.error} />}

      {log.data?.disabled && (
        <div className="banner warn">
          Logging is turned off, or no log file exists yet. Choose a level other
          than <strong>None</strong> in <Link to="/settings">Settings</Link>.
        </div>
      )}

      {log.data && !log.data.disabled && (
        <div className="card">
          <div className="small muted" style={{ marginBottom: '.5rem' }}>
            <span className="mono">{log.data.path}</span> ·{' '}
            {formatBytes(log.data.totalBytes)} · showing{' '}
            {log.data.lines.length} lines
          </div>
          <div className="scroll-x">
            <table className="logtable">
              <tbody>
                {log.data.lines.map((l, i) => (
                  <tr key={i} className={l.level ?? ''}>
                    <td className="ts">{l.timestamp ?? ''}</td>
                    <td className="lvl">{l.level ?? ''}</td>
                    <td className="logger">{l.logger ?? ''}</td>
                    <td>{l.message}</td>
                  </tr>
                ))}
                {log.data.lines.length === 0 && (
                  <tr>
                    <td colSpan={4} className="muted">
                      Nothing logged at this level yet.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  )
}
