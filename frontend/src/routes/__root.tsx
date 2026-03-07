import {
  createRootRouteWithContext,
  HeadContent,
  Outlet,
  Scripts,
} from '@tanstack/react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@/styles/app.css'
import { getCurrentUser, type CurrentUser } from '@/server/auth.functions'
import { defaultThemeId, themeStorageKey } from '@/lib/themes'

interface RouterContext {
  user: CurrentUser | null
}

const queryClient = new QueryClient()

export const Route = createRootRouteWithContext<RouterContext>()({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'PatchHound — Vulnerability Management' },
    ],
  }),
  beforeLoad: async () => {
    const user = await getCurrentUser()
    return { user }
  },
  component: RootDocument,
})

function RootDocument() {
  return (
    <html lang="en">
      <head>
        <HeadContent />
        <script
          dangerouslySetInnerHTML={{
            __html: `
              (function () {
                var storageKey = ${JSON.stringify(themeStorageKey)};
                var fallbackTheme = ${JSON.stringify(defaultThemeId)};
                var storedTheme = window.localStorage.getItem(storageKey) || fallbackTheme;
                var darkThemes = new Set([
                  'patchhound',
                  'solarized',
                  'cyberpunk',
                  'hackthebox',
                  'catppuccin-frappe',
                  'catppuccin-macchiato',
                  'catppuccin-mocha'
                ]);
                document.documentElement.dataset.theme = storedTheme;
                document.documentElement.classList.toggle('dark', darkThemes.has(storedTheme));
                document.documentElement.style.colorScheme = darkThemes.has(storedTheme) ? 'dark' : 'light';
              })();
            `,
          }}
        />
      </head>
      <body className="min-h-screen bg-background text-foreground antialiased">
        <QueryClientProvider client={queryClient}>
          <Outlet />
        </QueryClientProvider>
        <Scripts />
      </body>
    </html>
  )
}
