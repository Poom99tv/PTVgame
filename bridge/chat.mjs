export function normalizeChat(data) {
  return {
    comment: String(data.comment ?? data.content ?? ''),
    viewer: String(data.user?.uniqueId || data.user?.displayId || data.uniqueId || data.user?.userId || data.user?.id || 'unknown'),
    viewerDisplayName: String(data.user?.nickname || data.nickname || data.user?.displayId || data.user?.uniqueId || data.uniqueId || ''),
    messageId: String(data.msgId || data.common?.msgId || '') || undefined
  };
}
