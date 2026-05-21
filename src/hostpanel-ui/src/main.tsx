import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import * as Sentry from '@sentry/react'
import './index.css'
import App from './App'

// Sentry only initializes when VITE_SENTRY_DSN is baked in at build time.
// Installer's --sentry-ui-dsn flag writes .env.production before `vite build`.
const sentryDsn = import.meta.env.VITE_SENTRY_DSN as string | undefined
if (sentryDsn) {
  Sentry.init({
    dsn: sentryDsn,
    environment: import.meta.env.MODE,
    integrations: [Sentry.browserTracingIntegration(), Sentry.replayIntegration({
      maskAllText: true, blockAllMedia: true, // privacy: don't ship customer data
    })],
    tracesSampleRate: import.meta.env.MODE === 'development' ? 1.0 : 0.1,
    replaysSessionSampleRate: 0,        // never auto-record sessions
    replaysOnErrorSampleRate: 1.0,      // do record the seconds before an error
    sendDefaultPii: false,
    // Drop network/auth noise so the dashboard surfaces real bugs
    ignoreErrors: [
      'ResizeObserver loop limit exceeded',
      'Non-Error promise rejection captured',
      /Request aborted/,
    ],
  })
}

const FallbackUI = ({ resetError }: { resetError: () => void }) => (
  <div style={{ padding: 40, fontFamily: 'system-ui', maxWidth: 600, margin: '40px auto' }}>
    <h2 style={{ marginTop: 0 }}>Something went wrong.</h2>
    <p style={{ color: '#666' }}>The error has been reported. Try again, or reload the page.</p>
    <button onClick={resetError} style={{ padding: '8px 16px', cursor: 'pointer' }}>Try again</button>
  </div>
)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Sentry.ErrorBoundary fallback={FallbackUI}>
      <App />
    </Sentry.ErrorBoundary>
  </StrictMode>,
)
