type EventHandler = (event: string, data: unknown) => void

// In-memory map of connected clients
const clients = new Map<string, EventHandler>()

export function addClient(userId: string, handler: EventHandler): () => void {
  clients.set(userId, handler)
  return () => { clients.delete(userId) }
}

export function sendToUser(userId: string, event: string, data: unknown) {
  const handler = clients.get(userId)
  if (handler) {
    handler(event, data)
  }
}

export function broadcastToAll(event: string, data: unknown) {
  for (const handler of clients.values()) {
    handler(event, data)
  }
}
