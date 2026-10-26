# Packs the standalone Next.js build made by deploy.sh (site/); nothing is compiled on the server.
FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
COPY site/ .
USER node
EXPOSE 3000
CMD ["node", "server.js"]
