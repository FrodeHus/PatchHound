type EventHandler = (event: string, data: unknown) => void

let nextConnectionId = 0

// In-memory map of connected clients keyed by connection ID
const clients = new Map<number, { userId: string; handler: EventHandler }>()

export function addClient(userId: string, handler: EventHandler): () => void {
  const connectionId = nextConnectionId++
  clients.set(connectionId, { userId, handler })
  return () => { clients.delete(connectionId) }
}

export function sendToUser(userId: string, event: string, data: unknown) {
  for (const client of clients.values()) {
    if (client.userId === userId) {
      client.handler(event, data)
    }
  }
}

export function broadcastToAll(event: string, data: unknown) {
  for (const client of clients.values()) {
    client.handler(event, data)
  }
}
