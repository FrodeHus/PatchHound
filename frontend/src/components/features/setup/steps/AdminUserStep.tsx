type AdminUserStepProps = {
  adminEmail: string
  adminDisplayName: string
  adminEntraObjectId: string
  onAdminEmailChange: (value: string) => void
  onAdminDisplayNameChange: (value: string) => void
  onAdminEntraObjectIdChange: (value: string) => void
}

export function AdminUserStep({
  adminEmail,
  adminDisplayName,
  adminEntraObjectId,
  onAdminEmailChange,
  onAdminDisplayNameChange,
  onAdminEntraObjectIdChange,
}: AdminUserStepProps) {
  return (
    <section className="space-y-2 rounded-lg border border-border bg-card p-4">
      <h2 className="text-lg font-semibold">Admin User</h2>
      <input
        className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
        placeholder="Admin email"
        value={adminEmail}
        onChange={(event) => {
          onAdminEmailChange(event.target.value)
        }}
      />
      <input
        className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
        placeholder="Admin display name"
        value={adminDisplayName}
        onChange={(event) => {
          onAdminDisplayNameChange(event.target.value)
        }}
      />
      <input
        className="w-full rounded-md border border-input bg-background px-2 py-1.5 text-sm"
        placeholder="Admin Entra object ID"
        value={adminEntraObjectId}
        onChange={(event) => {
          onAdminEntraObjectIdChange(event.target.value)
        }}
      />
    </section>
  )
}
