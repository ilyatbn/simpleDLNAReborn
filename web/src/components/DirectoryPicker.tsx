import { useState } from 'react'
import { api } from '../api/client'
import { useAsync } from '../api/useAsync'
import { Modal, Spinner } from './ui'

/**
 * Replacement for FolderBrowserDialog, which a browser cannot open.
 *
 * Directories only - the picker never chooses files - with manual entry kept as
 * an escape hatch for UNC paths the tree cannot reach.
 */
export function DirectoryPicker({
  initialPath,
  onPick,
  onCancel,
}: {
  initialPath?: string | null
  onPick: (path: string) => void
  onCancel: () => void
}) {
  const [path, setPath] = useState<string | null>(initialPath ?? null)
  const [manual, setManual] = useState('')
  const listing = useAsync(() => api.browse(path), [path])

  const current = listing.data

  return (
    <Modal
      title="Choose a folder"
      onClose={onCancel}
      footer={
        <>
          <button onClick={onCancel}>Cancel</button>
          <button
            className="primary"
            disabled={!current?.path && !manual.trim()}
            onClick={() => onPick((manual.trim() || current?.path) as string)}
          >
            Select
          </button>
        </>
      }
    >
      <div className="stack">
        <div className="row">
          <button
            onClick={() => setPath(current?.parent ?? null)}
            disabled={!current?.path}
            title="Up one level"
          >
            ↑ Up
          </button>
          <span className="mono muted grow" style={{ flex: 1 }}>
            {current?.path ?? 'This PC'}
          </span>
        </div>

        {listing.loading && <Spinner />}
        {listing.error && (
          <div className="banner offline">{listing.error.message}</div>
        )}

        {current && (
          <ul className="itemlist" style={{ maxHeight: '18rem' }}>
            {current.entries.length === 0 && (
              <li className="empty">No subfolders</li>
            )}
            {current.entries.map((e) => (
              <li key={e.path}>
                <button
                  className="ghost grow"
                  style={{ justifyContent: 'flex-start', width: '100%' }}
                  disabled={!e.accessible}
                  title={
                    e.accessible ? e.path : `${e.path} — access denied`
                  }
                  onClick={() => setPath(e.path)}
                >
                  📁 {e.name}
                  {!e.accessible && (
                    <span className="muted small"> (no access)</span>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}

        <label className="field">
          Or type a path
          <input
            type="text"
            placeholder="\\\\server\\share\\media"
            value={manual}
            onChange={(e) => setManual(e.target.value)}
          />
        </label>
      </div>
    </Modal>
  )
}
