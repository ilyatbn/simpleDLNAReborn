import { useEffect, useState } from 'react'
import { api, ApiError } from '../../api/client'
import { useLive } from '../../api/live'
import { useAsync } from '../../api/useAsync'
import type { Settings } from '../../api/types'
import { DirectoryPicker } from '../../components/DirectoryPicker'
import {
  ConfirmDialog,
  ErrorState,
  Spinner,
  useToast,
} from '../../components/ui'

export function SettingsPage() {
  const toast = useToast()
  const { status, refreshStatus } = useLive()
  const loaded = useAsync(() => api.settings(), [])
  const caps = useAsync(() => api.capabilities(), [])

  const [form, setForm] = useState<Settings | null>(null)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)
  const [restart, setRestart] = useState<string[]>([])
  const [picking, setPicking] = useState(false)
  const [confirmDrop, setConfirmDrop] = useState(false)

  useEffect(() => {
    if (loaded.data) setForm(loaded.data)
  }, [loaded.data])

  const managed = status?.managed ?? true
  const tray = status?.host === 'tray'

  if (loaded.loading || caps.loading || !form) return <Spinner />
  if (loaded.error) return <ErrorState error={loaded.error} />

  const patch = (p: Partial<Settings>) =>
    setForm((f) => (f ? { ...f, ...p } : f))

  const save = async () => {
    setSaving(true)
    setErrors({})
    try {
      const rv = await api.saveSettings({
        port: form.port,
        cacheDir: form.cacheDir,
        rescanDelaySeconds: form.rescanDelaySeconds,
        rescanIntervalMinutes: form.rescanIntervalMinutes,
        logLevel: form.logLevel,
        preventSleep: form.preventSleep,
        ...(tray
          ? {
              startMinimized: form.startMinimized ?? false,
              autostart: form.autostart ?? false,
            }
          : {}),
      })
      setForm(rv)
      setRestart(rv.restartRequired ?? [])
      refreshStatus()
      toast.push('Settings saved')
    } catch (ex) {
      if (ex instanceof ApiError && ex.details.length) {
        const grouped: Record<string, string[]> = {}
        for (const d of ex.details) (grouped[d.field] ??= []).push(d.message)
        setErrors(grouped)
        toast.push('Please fix the highlighted fields', true)
      } else {
        toast.push(ex instanceof Error ? ex.message : String(ex), true)
      }
    } finally {
      setSaving(false)
    }
  }

  const dropCache = async () => {
    setConfirmDrop(false)
    try {
      await api.dropCache()
      toast.push('Cache dropped; servers are restarting')
    } catch (ex) {
      toast.push(ex instanceof Error ? ex.message : String(ex), true)
    }
  }

  return (
    <>
      <div className="page-head">
        <h1>Settings</h1>
        <div className="spacer" />
        <button onClick={() => loaded.reload()} disabled={saving}>
          Cancel
        </button>
        <button
          className="primary"
          onClick={save}
          disabled={saving || !managed}
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>

      {!managed && (
        <div className="banner warn" style={{ marginBottom: '1rem' }}>
          Settings come from the command line in this mode and cannot be
          changed here.
        </div>
      )}

      {restart.length > 0 && (
        <div className="banner warn" style={{ marginBottom: '1rem' }}>
          Saved. {restart.join(' and ')}{' '}
          {restart.length === 1 ? 'takes' : 'take'} effect the next time
          SimpleDLNA starts.
        </div>
      )}

      <div className="stack">
        <fieldset>
          <legend>Port</legend>
          <div className="stack">
            <label className="field" style={{ maxWidth: '12rem' }}>
              DLNA server port
              <input
                type="number"
                min={0}
                max={65535}
                value={form.port}
                onChange={(e) => patch({ port: Number(e.target.value) })}
              />
            </label>
            <div className="small muted">
              0 picks a free port at startup. Currently listening on{' '}
              <strong>{form.effective.port}</strong>. Changing this requires a
              restart.
            </div>
            <FieldErrors errors={errors.port} />
          </div>
        </fieldset>

        <fieldset>
          <legend>Cache directory</legend>
          <div className="stack">
            <div className="row">
              <input
                type="text"
                style={{ flex: 1 }}
                placeholder="(default)"
                value={form.cacheDir}
                onChange={(e) => patch({ cacheDir: e.target.value })}
              />
              <button onClick={() => setPicking(true)}>Browse…</button>
            </div>
            <div className="small muted">
              Holds the media cache and the log. Currently{' '}
              <span className="mono">{form.effective.cacheDir}</span>.
              Configuration is always stored in{' '}
              <span className="mono">{status?.configDir}</span>. Changing this
              requires a restart.
            </div>
            <div className="row">
              <button className="danger" onClick={() => setConfirmDrop(true)}>
                Drop cache
              </button>
              <span className="small muted">
                Deletes the metadata cache and restarts every running server.
              </span>
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>Library refresh</legend>
          <div className="stack">
            <label className="field" style={{ maxWidth: '20rem' }}>
              Seconds after a change is detected
              <input
                type="number"
                min={1}
                max={3600}
                value={form.rescanDelaySeconds}
                onChange={(e) =>
                  patch({ rescanDelaySeconds: Number(e.target.value) })
                }
              />
            </label>
            <FieldErrors errors={errors.rescanDelaySeconds} />
            <label className="field" style={{ maxWidth: '20rem' }}>
              Minutes between full rescans (0 = off)
              <input
                type="number"
                min={0}
                max={1440}
                value={form.rescanIntervalMinutes}
                onChange={(e) =>
                  patch({ rescanIntervalMinutes: Number(e.target.value) })
                }
              />
            </label>
            <FieldErrors errors={errors.rescanIntervalMinutes} />
            <div className="small muted">
              Both apply the next time a server is restarted — use{' '}
              <strong>Restart</strong> on the Servers page to apply them now.
            </div>
          </div>
        </fieldset>

        <fieldset>
          <legend>Logging</legend>
          <div className="stack">
            <label className="field" style={{ maxWidth: '14rem' }}>
              Detail written to sdlna.log
              <select
                value={form.logLevel}
                onChange={(e) => patch({ logLevel: e.target.value })}
              >
                {(caps.data?.logLevels ?? []).map((l) => (
                  <option key={l} value={l}>
                    {l}
                  </option>
                ))}
              </select>
            </label>
            <div className="small muted">
              None turns logging off entirely. Debug is very noisy.
            </div>
            <FieldErrors errors={errors.logLevel} />
          </div>
        </fieldset>

        <fieldset>
          <legend>Behaviour</legend>
          <div className="stack">
            <label className="check">
              <input
                type="checkbox"
                checked={form.preventSleep}
                onChange={(e) => patch({ preventSleep: e.target.checked })}
              />
              Prevent sleep while playing
            </label>
            {tray && (
              <>
                <label className="check">
                  <input
                    type="checkbox"
                    checked={form.startMinimized ?? false}
                    onChange={(e) =>
                      patch({ startMinimized: e.target.checked })
                    }
                  />
                  Start minimized
                </label>
                <label className="check">
                  <input
                    type="checkbox"
                    checked={form.autostart ?? false}
                    onChange={(e) => patch({ autostart: e.target.checked })}
                  />
                  Start automatically with Windows
                </label>
              </>
            )}
          </div>
        </fieldset>
      </div>

      {picking && (
        <DirectoryPicker
          initialPath={form.effective.cacheDir}
          onCancel={() => setPicking(false)}
          onPick={(p) => {
            setPicking(false)
            patch({ cacheDir: p })
          }}
        />
      )}

      {confirmDrop && (
        <ConfirmDialog
          title="Drop cache"
          message="Are you sure you want to drop the cache?"
          confirmLabel="Drop cache"
          danger
          onConfirm={dropCache}
          onCancel={() => setConfirmDrop(false)}
        />
      )}
    </>
  )
}

function FieldErrors({ errors }: { errors?: string[] }) {
  if (!errors?.length) return null
  return (
    <div className="error-text" role="alert">
      {errors.join('. ')}
    </div>
  )
}
