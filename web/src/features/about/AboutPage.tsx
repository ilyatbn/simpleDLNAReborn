import { useLive } from '../../api/live'
import { Spinner } from '../../components/ui'

export function AboutPage() {
  const { status } = useLive()
  if (!status) return <Spinner />

  return (
    <>
      <div className="page-head">
        <h1>About</h1>
      </div>
      <div className="card stack">
        <div>
          <h2>SimpleDLNA</h2>
          <div className="muted">Version {status.version}</div>
        </div>
        <dl style={{ margin: 0, display: 'grid', gap: '.35rem' }}>
          <Row label="HTTP signature" value={status.signature} mono />
          <Row label="Media port" value={String(status.mediaPort)} />
          <Row label="Admin port" value={String(status.adminPort)} />
          <Row label="Configuration" value={status.configDir} mono />
          <Row label="Cache" value={status.cacheDir} mono />
          <Row
            label="Mode"
            value={
              status.managed
                ? `${status.host} · managed`
                : `${status.host} · command line`
            }
          />
        </dl>
        <div className="row">
          <a
            className="button"
            href="http://nmaier.github.io/simpleDLNA/"
            target="_blank"
            rel="noreferrer noopener"
          >
            Project homepage
          </a>
          <a
            className="button"
            href={status.browseUrl}
            target="_blank"
            rel="noreferrer noopener"
          >
            Browse media
          </a>
        </div>
        <p className="small muted" style={{ margin: 0 }}>
          Released under the terms of the LICENSE file shipped with this
          program.
        </p>
      </div>
    </>
  )
}

function Row({
  label,
  value,
  mono,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div
      style={{
        display: 'flex',
        gap: '.75rem',
        flexWrap: 'wrap',
        borderBottom: '1px solid var(--border)',
        paddingBottom: '.35rem',
      }}
    >
      <dt className="muted small" style={{ minWidth: '9rem' }}>
        {label}
      </dt>
      <dd
        className={mono ? 'mono' : undefined}
        style={{ margin: 0, wordBreak: 'break-all' }}
      >
        {value}
      </dd>
    </div>
  )
}
