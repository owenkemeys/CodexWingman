(() => {
  const controller = window.__codexHelperHookTrace
  if (controller?.cleanup) return controller.cleanup()
  document.querySelectorAll('[data-hook-trace-actions-active]').forEach((node) => node.removeAttribute('data-hook-trace-actions-active'))
  document.querySelectorAll('[data-hook-trace-visibility-active]').forEach((node) => node.removeAttribute('data-hook-trace-visibility-active'))
  document.querySelectorAll('[data-hook-trace-native-suppressed]').forEach((node) => node.removeAttribute('data-hook-trace-native-suppressed'))
  document.querySelectorAll('[data-codex-wingman-owner="hook-trace"]').forEach((node) => node.remove())
  return true
})()
