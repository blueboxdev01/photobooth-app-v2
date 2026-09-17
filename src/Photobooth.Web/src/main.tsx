import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

// Self-hosted rather than fetched from a font CDN: a booth at a venue with no
// internet would otherwise fall back to system fonts mid-event.
import '@fontsource-variable/manrope'
import '@fontsource-variable/jetbrains-mono'

import { Diagnostics } from './Diagnostics'
import { Display } from './Display'
import { Operator } from './Operator'
import { Templates } from './Templates'
import './styles.css'

// A handful of screens, one bundle. No router: each is opened directly as its
// own browser window or tab, so the path is enough.
//
// /guest and /display are the same screen under two names. /guest is what the
// iPad is set up with and what the docs say; /display is kept because it is
// written on things and in older notes, and a dead URL on the guest-facing
// device is a bad way to find that out.
const path = window.location.pathname.replace(/\/+$/, '')
const view =
  path === '/guest' || path === '/display' ? <Display />
  : path === '/diagnostics' ? <Diagnostics />
  : path === '/templates' ? <Templates />
  : <Operator />

createRoot(document.getElementById('root')!).render(<StrictMode>{view}</StrictMode>)
