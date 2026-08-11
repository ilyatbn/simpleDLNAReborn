import { useCallback, useEffect, useState } from 'react'

export interface AsyncState<T> {
  data: T | undefined
  error: Error | undefined
  loading: boolean
  reload: () => void
}

/**
 * Minimal data-fetching hook.
 *
 * The app has six resources and invalidation is driven explicitly by SSE, so a
 * caching library would be more machinery than the problem needs.
 */
export function useAsync<T>(
  load: () => Promise<T>,
  deps: unknown[] = [],
): AsyncState<T> {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<Error>()
  const [loading, setLoading] = useState(true)
  const [nonce, setNonce] = useState(0)

  const reload = useCallback(() => setNonce((n) => n + 1), [])

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    load()
      .then((rv) => {
        if (cancelled) return
        setData(rv)
        setError(undefined)
      })
      .catch((ex: Error) => {
        if (cancelled) return
        setError(ex)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce])

  return { data, error, loading, reload }
}
