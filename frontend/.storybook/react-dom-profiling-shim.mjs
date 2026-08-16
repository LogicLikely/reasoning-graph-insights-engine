import * as React from 'react'
import * as ReactDOM from 'react-dom/profiling'

const roots = new Map()

function getIsReactActEnvironment() {
  return globalThis.IS_REACT_ACT_ENVIRONMENT
}

function WithCallback({ callback, children }) {
  const once = React.useRef()

  React.useLayoutEffect(() => {
    if (once.current !== callback) {
      once.current = callback
      callback()
    }
  }, [callback])

  return children
}

if (typeof Promise.withResolvers === 'undefined') {
  Promise.withResolvers = () => {
    let resolve
    let reject
    const promise = new Promise((promiseResolve, promiseReject) => {
      resolve = promiseResolve
      reject = promiseReject
    })
    return { promise, resolve, reject }
  }
}

async function getReactRoot(element, rootOptions) {
  let root = roots.get(element)
  if (!root) {
    root = ReactDOM.createRoot(element, rootOptions)
    roots.set(element, root)
  }
  return root
}

export async function renderElement(node, element, rootOptions) {
  const root = await getReactRoot(element, rootOptions)
  if (getIsReactActEnvironment()) {
    root.render(node)
    return
  }

  const { promise, resolve } = Promise.withResolvers()
  root.render(React.createElement(WithCallback, { callback: resolve }, node))
  return promise
}

export function unmountElement(element) {
  const root = roots.get(element)
  if (root) {
    root.unmount()
    roots.delete(element)
  }
}
