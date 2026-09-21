function refresh(wingman) {
  var snapshot = wingman.files.codexTargetSession.hookTrace();
  if (!snapshot || typeof snapshot !== 'object' || Array.isArray(snapshot)) {
    return { threadId: null, status: 'session-unavailable', records: {} };
  }
  var records = snapshot.records;
  return {
    threadId: typeof snapshot.threadId === 'string' ? snapshot.threadId : null,
    status: snapshot.status === 'available' ? 'available' : 'session-unavailable',
    records: records && typeof records === 'object' && !Array.isArray(records) ? records : {}
  };
}
