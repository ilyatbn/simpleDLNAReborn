import { useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError } from '../../api/client'
import { useLive } from '../../api/live'
import type { Server } from '../../api/types'
import { ConfirmDialog, Spinner, StateBadge, useToast } from '../../components/ui'

export function ServersPage() {
  const { servers, status, refreshServers } = useLive()
  const toast = useToast()
  const [confirmRemove, setConfirmRemove] = useState<Server | null>(null)
  const [busy, setBusy] = useState<string | null>(null)

  const managed = status?.managed ?? true

  const act = async (id: string, what: () => Promise<unknown>) => {
    setBusy(id)
    try {
      await what()
      refreshServers()
    } catch (ex) {
      toast.push(
        ex instanceof ApiError ? ex.message : String(ex),
        true,
      )
      refreshServers()
    } finally {
      setBusy(null)
    }
  }

  const remove = async (server: Server) => {
    setConfirmRemove(null)
    await act(server.id, () => api.deleteServer(server.id))
    toast.push(`Removed ${server.name}`)
  }

  const rescanAll = async () => {
    try {
      const rv = await api.rescanAll()
      toast.push(
        rv.skipped === 0
          ? `Rescanning ${rv.requested} server${rv.requested === 1 ? '' : 's'}`
          : `Rescanning ${rv.requested}, skipped ${rv.skipped} (not running)`,
      )
    } catch (ex) {
      toast.push(ex instanceof ApiError ? ex.message : String(ex), true)
    }
  }

  if (!servers) return <Spinner />

  return (
    <>
      <div className="page-head">
        <h1>Servers</h1>
        <div className="spacer" />
        <button onClick={rescanAll} disabled={servers.length === 0}>
          Rescan all
        </button>
        {managed && (
          <Link className="button primary" to="/servers/new">
            New server
          </Link>
        )}
      </div>

      {!managed && (
        <div className="banner warn" style={{ marginBottom: '1rem' }}>
          This server is configured from the command line, so servers and
          settings cannot be changed here. Restart with{' '}
          <code className="mono">--managed</code> to manage them from this
          interface.
        </div>
      )}

      {servers.length === 0 ? (
        <div className="empty-state">
          <h2>No servers yet</h2>
          <p>
            A server shares one or more folders with the devices on your
            network.
          </p>
          {managed && (
            <Link className="button primary" to="/servers/new">
              Add your first server
            </Link>
          )}
        </div>
      ) : (
        <div className="stack">
          {servers.map((s) => (
            <div className="server" key={s.id}>
              <div className="grow">
                <div className="row">
                  <span className="title">{s.name}</span>
                  <StateBadge state={s.state} error={s.lastError} />
                </div>
                <div className="meta">
                  {s.directories.length}{' '}
                  {s.directories.length === 1 ? 'folder' : 'folders'}
                  {' · '}
                  {s.types.join(', ') || 'no types'}
                  {s.views.length > 0 && ` · ${s.views.join(', ')}`}
                  {s.loadSeconds != null &&
                    ` · loaded in ${s.loadSeconds.toFixed(2)}s`}
                  {s.mountPrefix && ` · ${s.mountPrefix}`}
                </div>
                {s.lastError && (
                  <div className="server-error">{s.lastError}</div>
                )}
              </div>
              <div className="actions">
                <button
                  disabled={busy === s.id}
                  onClick={() =>
                    act(s.id, () =>
                      s.state === 'stopped' || s.state === 'idle'
                        ? api.startServer(s.id)
                        : api.stopServer(s.id),
                    )
                  }
                >
                  {s.state === 'stopped' || s.state === 'idle'
                    ? 'Start'
                    : 'Stop'}
                </button>
                <button
                  disabled={s.state !== 'running' || busy === s.id}
                  title={
                    s.state === 'running'
                      ? 'Stop and start again, picking up changed refresh settings'
                      : 'Only available while running'
                  }
                  onClick={() => act(s.id, () => api.restartServer(s.id))}
                >
                  Restart
                </button>
                <button
                  disabled={s.state !== 'running' || busy === s.id}
                  title={
                    s.state === 'running'
                      ? 'Rescan this library'
                      : 'Only available while running'
                  }
                  onClick={() => act(s.id, () => api.rescanServer(s.id))}
                >
                  Rescan
                </button>
                {managed && (
                  <>
                    <Link className="button" to={`/servers/${s.id}`}>
                      Edit
                    </Link>
                    <button
                      className="danger"
                      onClick={() => setConfirmRemove(s)}
                    >
                      Remove
                    </button>
                  </>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {confirmRemove && (
        <ConfirmDialog
          title="Remove server"
          message={`Would you like to remove ${confirmRemove.name}?`}
          confirmLabel="Remove"
          danger
          onConfirm={() => remove(confirmRemove)}
          onCancel={() => setConfirmRemove(null)}
        />
      )}
    </>
  )
}
