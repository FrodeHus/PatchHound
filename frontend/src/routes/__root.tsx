import {
  createRootRouteWithContext,
  HeadContent,
  Outlet,
  Scripts,
} from '@tanstack/react-router'
import { RouteError } from '@/components/ui/error-boundary'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import '@/styles/app.css'
import { Toaster } from 'sonner'
import { getCurrentUser, type CurrentUser } from '@/server/auth.functions'
import { defaultThemeId, themeStorageKey, themeOptions } from '@/lib/themes'
import { TooltipProvider } from '@/components/ui/tooltip'

interface RouterContext {
  user: CurrentUser | null
}

const queryClient = new QueryClient()
const darkThemeIds = themeOptions.filter(t => t.mode === 'dark').map(t => t.id)

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
  errorComponent: RouteError,
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
                var darkThemes = new Set(${JSON.stringify(darkThemeIds)});
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
          <TooltipProvider>
            <Outlet />
          </TooltipProvider>
          <Toaster richColors position="bottom-right" />
        </QueryClientProvider>
        <Scripts />
      </body>
    </html>
  )
}
