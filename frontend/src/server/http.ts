export function redirectResponse(location: string, status = 302): Response {
  return new Response(null, {
    status,
    headers: {
      Location: location,
    },
  })
}
