import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, ApiError } from '../../api/client'
import { useLive } from '../../api/live'
import { useAsync } from '../../api/useAsync'
import type { NamedItem, ServerInput } from '../../api/types'
import { DirectoryPicker } from '../../components/DirectoryPicker'
import { ErrorState, Spinner, useToast } from '../../components/ui'

const EMPTY: ServerInput = {
  name: '',
  order: 'title',
  orderDescending: false,
  types: ['video'],
  views: [],
  directories: [],
  restrictions: { mac: [], ip: [], userAgent: [] },
}

type RestrictionKind = 'mac' | 'ip' | 'userAgent'

const RESTRICTION_LABELS: Record<RestrictionKind, string> = {
  mac: 'MAC',
  ip: 'IP',
  userAgent: 'User-Agent',
}

export function ServerEditorPage() {
  const { id } = useParams()
  const isNew = !id
  const navigate = useNavigate()
  const toast = useToast()
  const { refreshServers } = useLive()

  const caps = useAsync(() => api.capabilities(), [])
  const existing = useAsync(
    () => (isNew ? Promise.resolve(null) : api.server(id!)),
    [id],
  )

  const [form, setForm] = useState<ServerInput>(EMPTY)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [saving, setSaving] = useState(false)
  const [wasRunning, setWasRunning] = useState(false)
  const [picking, setPicking] = useState(false)

  useEffect(() => {
    const s = existing.data
    if (!s) return
    setWasRunning(s.state === 'running' || s.state === 'refreshing')
    setForm({
      name: s.name,
      order: s.order,
      orderDescending: s.orderDescending,
      types: s.types,
      views: s.views,
      directories: s.directories,
      restrictions: {
        mac: s.restrictions.mac ?? [],
        ip: s.restrictions.ip ?? [],
        userAgent: s.restrictions.userAgent ?? [],
      },
    })
  }, [existing.data])

  const patch = (p: Partial<ServerInput>) => setForm((f) => ({ ...f, ...p }))

  const save = async () => {
    setSaving(true)
    setErrors({})
    try {
      if (isNew) {
        await api.createServer(form)
        toast.push(`Created ${form.name}`)
      } else {
        await api.updateServer(id!, form)
        toast.push(`Saved ${form.name}`)
      }
      refreshServers()
      navigate('/')
    } catch (ex) {
      if (ex instanceof ApiError && ex.details.length > 0) {
        const grouped: Record<string, string[]> = {}
        for (const d of ex.details) {
          ;(grouped[d.field] ??= []).push(d.message)
        }
        setErrors(grouped)
        toast.push('Please fix the highlighted fields', true)
      } else {
        toast.push(ex instanceof Error ? ex.message : String(ex), true)
      }
    } finally {
      setSaving(false)
    }
  }

  if (caps.loading || existing.loading) return <Spinner />
  if (caps.error) return <ErrorState error={caps.error} />
  if (existing.error) return <ErrorState error={existing.error} />

  return (
    <>
      <div className="page-head">
        <h1>{isNew ? 'New server' : 'Edit server'}</h1>
        <div className="spacer" />
        <button onClick={() => navigate('/')}>Cancel</button>
        <button className="primary" onClick={save} disabled={saving}>
          {saving ? 'Saving…' : 'Save'}
        </button>
      </div>

      {!isNew && wasRunning && (
        <div className="banner warn" style={{ marginBottom: '1rem' }}>
          This server is running. Saving will restart it, briefly interrupting
          any playback.
        </div>
      )}

      <div className="stack">
        <fieldset>
          <legend>Name</legend>
          <input
            type="text"
            value={form.name}
            autoFocus
            onChange={(e) => patch({ name: e.target.value })}
          />
          <FieldErrors errors={errors.name} />
        </fieldset>

        <fieldset>
          <legend>Order</legend>
          <div className="row">
            <select
              style={{ flex: 1 }}
              value={form.order}
              onChange={(e) => patch({ order: e.target.value })}
            >
              {caps.data!.orders.map((o) => (
                <option key={o.name} value={o.name}>
                  {o.name} — {o.description}
                </option>
              ))}
            </select>
            <label className="check">
              <input
                type="checkbox"
                checked={form.orderDescending}
                onChange={(e) => patch({ orderDescending: e.target.checked })}
              />
              Descending
            </label>
          </div>
          <FieldErrors errors={errors.order} />
        </fieldset>

        <fieldset>
          <legend>Types</legend>
          <div className="row">
            {caps.data!.mediaTypes.map((t) => (
              <label className="check" key={t}>
                <input
                  type="checkbox"
                  checked={form.types.includes(t)}
                  onChange={(e) =>
                    patch({
                      types: e.target.checked
                        ? [...form.types, t]
                        : form.types.filter((x) => x !== t),
                    })
                  }
                />
                {t === 'image' ? 'Images' : t[0].toUpperCase() + t.slice(1)}
              </label>
            ))}
          </div>
          <FieldErrors errors={errors.types} />
        </fieldset>

        <ViewsSection
          views={caps.data!.views}
          value={form.views}
          errors={errors.views}
          onChange={(views) => patch({ views })}
        />

        <RestrictionsSection
          value={form.restrictions}
          errors={errors}
          onChange={(restrictions) => patch({ restrictions })}
        />

        <fieldset>
          <legend>Directories</legend>
          <div className="stack">
            <ul className="itemlist">
              {form.directories.length === 0 && (
                <li className="empty">No folders yet</li>
              )}
              {form.directories.map((d) => (
                <li key={d}>
                  <span className="grow mono">{d}</span>
                  <button
                    className="ghost"
                    aria-label={`Remove ${d}`}
                    onClick={() =>
                      patch({
                        directories: form.directories.filter((x) => x !== d),
                      })
                    }
                  >
                    ✕
                  </button>
                </li>
              ))}
            </ul>
            <div className="row">
              <button onClick={() => setPicking(true)}>Add folder…</button>
            </div>
            <FieldErrors errors={errors.directories} />
          </div>
        </fieldset>
      </div>

      {picking && (
        <DirectoryPicker
          onCancel={() => setPicking(false)}
          onPick={(p) => {
            setPicking(false)
            if (!form.directories.includes(p)) {
              patch({ directories: [...form.directories, p] })
            }
          }}
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

/**
 * Views are ordered and may carry parameters. The old dialog could express
 * neither: its Up/Down buttons were wired to nothing, and a parameterised view
 * name made the editor throw on load.
 */
function ViewsSection({
  views,
  value,
  errors,
  onChange,
}: {
  views: NamedItem[]
  value: string[]
  errors?: string[]
  onChange: (views: string[]) => void
}) {
  const [selected, setSelected] = useState(views[0]?.name ?? '')
  const [params, setParams] = useState<Record<string, string>>({})

  const chosen = useMemo(
    () => views.find((v) => v.name === selected),
    [views, selected],
  )

  const add = () => {
    if (!selected) return
    const pairs = Object.entries(params)
      .filter(([, v]) => v.trim() !== '')
      .map(([k, v]) => `${k}=${v.trim()}`)
    const spec = pairs.length ? `${selected}:${pairs.join(',')}` : selected
    if (!value.includes(spec)) {
      onChange([...value, spec])
    }
    setParams({})
  }

  const move = (index: number, delta: number) => {
    const next = [...value]
    const target = index + delta
    if (target < 0 || target >= next.length) return
    ;[next[index], next[target]] = [next[target], next[index]]
    onChange(next)
  }

  return (
    <fieldset>
      <legend>Views</legend>
      <div className="stack">
        <ul className="itemlist">
          {value.length === 0 && (
            <li className="empty">No views — files are shown as they are</li>
          )}
          {value.map((v, i) => (
            <li key={v}>
              <span className="grow mono">{v}</span>
              <button
                className="ghost"
                aria-label={`Move ${v} up`}
                disabled={i === 0}
                onClick={() => move(i, -1)}
              >
                ↑
              </button>
              <button
                className="ghost"
                aria-label={`Move ${v} down`}
                disabled={i === value.length - 1}
                onClick={() => move(i, 1)}
              >
                ↓
              </button>
              <button
                className="ghost"
                aria-label={`Remove ${v}`}
                onClick={() => onChange(value.filter((x) => x !== v))}
              >
                ✕
              </button>
            </li>
          ))}
        </ul>

        <div className="row">
          <select
            style={{ flex: 1 }}
            value={selected}
            onChange={(e) => {
              setSelected(e.target.value)
              setParams({})
            }}
          >
            {views.map((v) => (
              <option key={v.name} value={v.name}>
                {v.name} — {v.description}
              </option>
            ))}
          </select>
          <button onClick={add}>Add</button>
        </div>

        {chosen?.configurable && chosen.parameters && (
          <div className="card" style={{ background: 'var(--surface-2)' }}>
            <div className="small muted" style={{ marginBottom: '.5rem' }}>
              Options for <strong>{chosen.name}</strong> — leave blank for
              defaults
            </div>
            <div className="stack">
              {chosen.parameters.map((p) => (
                <label className="field" key={p.name}>
                  {p.name}
                  {p.unit ? ` (${p.unit})` : ''}
                  {p.default ? ` — default ${p.default}` : ''}
                  <input
                    type="text"
                    placeholder={p.description ?? ''}
                    value={params[p.name] ?? ''}
                    onChange={(e) =>
                      setParams((x) => ({ ...x, [p.name]: e.target.value }))
                    }
                  />
                </label>
              ))}
            </div>
          </div>
        )}
        <FieldErrors errors={errors} />
      </div>
    </fieldset>
  )
}

function RestrictionsSection({
  value,
  errors,
  onChange,
}: {
  value: Record<RestrictionKind, string[]>
  errors: Record<string, string[]>
  onChange: (v: Record<RestrictionKind, string[]>) => void
}) {
  const [kind, setKind] = useState<RestrictionKind>('mac')
  const [text, setText] = useState('')

  const add = () => {
    const v = text.trim()
    if (!v || value[kind].includes(v)) return
    onChange({ ...value, [kind]: [...value[kind], v] })
    setText('')
  }

  const all = (Object.keys(RESTRICTION_LABELS) as RestrictionKind[]).flatMap(
    (k) => value[k].map((v) => ({ kind: k, value: v })),
  )

  return (
    <fieldset>
      <legend>Restrictions</legend>
      <div className="stack">
        <div className="small muted">
          Leave empty to allow every device. Adding any restriction limits
          access to the listed ones.
        </div>
        <ul className="itemlist">
          {all.length === 0 && <li className="empty">No restrictions</li>}
          {all.map((r) => (
            <li key={`${r.kind}:${r.value}`}>
              <span className="grow mono">{r.value}</span>
              <span className="badge idle">{RESTRICTION_LABELS[r.kind]}</span>
              <button
                className="ghost"
                aria-label={`Remove ${r.value}`}
                onClick={() =>
                  onChange({
                    ...value,
                    [r.kind]: value[r.kind].filter((x) => x !== r.value),
                  })
                }
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
        <div className="row">
          <input
            type="text"
            style={{ flex: 1 }}
            value={text}
            placeholder={
              kind === 'mac'
                ? '01:AF:BC:00:0A:FF'
                : kind === 'ip'
                  ? '192.168.1.44'
                  : 'Some Media Player/1.0'
            }
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                add()
              }
            }}
          />
          <select
            value={kind}
            onChange={(e) => setKind(e.target.value as RestrictionKind)}
            style={{ width: 'auto' }}
          >
            {(Object.keys(RESTRICTION_LABELS) as RestrictionKind[]).map((k) => (
              <option key={k} value={k}>
                {RESTRICTION_LABELS[k]}
              </option>
            ))}
          </select>
          <button onClick={add}>Add</button>
        </div>
        <FieldErrors
          errors={[
            ...(errors['restrictions.mac'] ?? []),
            ...(errors['restrictions.ip'] ?? []),
            ...(errors['restrictions.userAgent'] ?? []),
          ]}
        />
      </div>
    </fieldset>
  )
}
