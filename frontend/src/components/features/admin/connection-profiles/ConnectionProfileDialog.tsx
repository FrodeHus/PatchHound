import { useState, useEffect } from 'react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import type { ConnectionProfile } from '@/api/authenticated-scans.schemas'

type Props = {
  open: boolean
  onOpenChange: (open: boolean) => void
  profile?: ConnectionProfile | null
  onSubmit: (data: {
    name: string
    description: string
    sshHost: string
    sshPort: number
    sshUsername: string
    authMethod: 'password' | 'privateKey'
    password?: string
    privateKey?: string
    passphrase?: string
    hostKeyFingerprint?: string
  }) => void
  isPending: boolean
}

export function ConnectionProfileDialog({ open, onOpenChange, profile, onSubmit, isPending }: Props) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [sshHost, setSshHost] = useState('')
  const [sshPort, setSshPort] = useState(22)
  const [sshUsername, setSshUsername] = useState('')
  const [authMethod, setAuthMethod] = useState<'password' | 'privateKey'>('password')
  const [password, setPassword] = useState('')
  const [privateKey, setPrivateKey] = useState('')
  const [passphrase, setPassphrase] = useState('')
  const [hostKeyFingerprint, setHostKeyFingerprint] = useState('')

  useEffect(() => {
    if (profile) {
      setName(profile.name)
      setDescription(profile.description)
      setSshHost(profile.sshHost)
      setSshPort(profile.sshPort)
      setSshUsername(profile.sshUsername)
      setAuthMethod(profile.authMethod as 'password' | 'privateKey')
      setHostKeyFingerprint(profile.hostKeyFingerprint ?? '')
    } else {
      setName('')
      setDescription('')
      setSshHost('')
      setSshPort(22)
      setSshUsername('')
      setAuthMethod('password')
      setHostKeyFingerprint('')
    }
    setPassword('')
    setPrivateKey('')
    setPassphrase('')
  }, [profile, open])

  const isEdit = Boolean(profile)
  const canSubmit = name && sshHost && sshUsername && (isEdit || (authMethod === 'password' ? password : privateKey))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{isEdit ? 'Edit' : 'New'} Connection Profile</DialogTitle>
          <DialogDescription>SSH connection details for target hosts.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4 max-h-[60vh] overflow-y-auto pr-1">
          <div>
            <Label>Name</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="prod-linux-servers" />
          </div>
          <div>
            <Label>Description</Label>
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div className="col-span-2">
              <Label>SSH Host</Label>
              <Input value={sshHost} onChange={(e) => setSshHost(e.target.value)} placeholder="10.0.1.50" />
            </div>
            <div>
              <Label>Port</Label>
              <Input type="number" value={sshPort} onChange={(e) => setSshPort(Number(e.target.value))} />
            </div>
          </div>
          <div>
            <Label>Username</Label>
            <Input value={sshUsername} onChange={(e) => setSshUsername(e.target.value)} placeholder="scanner" />
          </div>
          <div>
            <Label>Auth Method</Label>
            <div className="mt-1 flex gap-4">
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" checked={authMethod === 'password'} onChange={() => setAuthMethod('password')} />
                Password
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="radio" checked={authMethod === 'privateKey'} onChange={() => setAuthMethod('privateKey')} />
                Private Key
              </label>
            </div>
          </div>
          {authMethod === 'password' ? (
            <div>
              <Label>{isEdit ? 'New Password (leave blank to keep)' : 'Password'}</Label>
              <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
          ) : (
            <>
              <div>
                <Label>{isEdit ? 'New Private Key (leave blank to keep)' : 'Private Key'}</Label>
                <Textarea value={privateKey} onChange={(e) => setPrivateKey(e.target.value)} rows={4} className="font-mono text-xs" placeholder="-----BEGIN RSA PRIVATE KEY-----" />
              </div>
              <div>
                <Label>Passphrase (optional)</Label>
                <Input type="password" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} />
              </div>
            </>
          )}
          <div>
            <Label>Host Key Fingerprint (optional)</Label>
            <Input value={hostKeyFingerprint} onChange={(e) => setHostKeyFingerprint(e.target.value)} placeholder="SHA256:..." />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <Button
            onClick={() =>
              onSubmit({
                name,
                description,
                sshHost,
                sshPort,
                sshUsername,
                authMethod,
                ...(password ? { password } : {}),
                ...(privateKey ? { privateKey } : {}),
                ...(passphrase ? { passphrase } : {}),
                ...(hostKeyFingerprint ? { hostKeyFingerprint } : {}),
              })
            }
            disabled={!canSubmit || isPending}
          >
            {isEdit ? 'Save' : 'Create'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
