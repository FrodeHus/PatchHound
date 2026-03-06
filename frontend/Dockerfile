FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
ARG VITE_API_URL=/api
ARG VITE_SIGNALR_URL=
ARG VITE_ENTRA_CLIENT_ID=
ARG VITE_ENTRA_AUTHORITY=https://login.microsoftonline.com/common
ARG VITE_API_SCOPES=
ENV VITE_API_URL=${VITE_API_URL}
ENV VITE_SIGNALR_URL=${VITE_SIGNALR_URL}
ENV VITE_ENTRA_CLIENT_ID=${VITE_ENTRA_CLIENT_ID}
ENV VITE_ENTRA_AUTHORITY=${VITE_ENTRA_AUTHORITY}
ENV VITE_API_SCOPES=${VITE_API_SCOPES}
RUN npm run build

FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
