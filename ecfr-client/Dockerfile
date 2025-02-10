FROM node:20 AS builder

WORKDIR /app
COPY package*.json ./
RUN npm install
COPY . .
RUN npm run build -- --configuration production

FROM nginx:alpine
COPY --from=builder /app/dist/ecfr-client/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/templates/default.conf.template
ENV API_HOST=ecfr-app-api.whitemeadow-0306cf91.eastus.azurecontainerapps.io

CMD ["/bin/sh", "-c", "envsubst '${API_HOST}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
