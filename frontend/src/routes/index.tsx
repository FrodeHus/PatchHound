import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/')({
  component: () => (
    <div className="p-4">
      <h1 className="text-2xl font-bold">Vigil</h1>
      <p>Vulnerability Management Platform</p>
    </div>
  ),
})
