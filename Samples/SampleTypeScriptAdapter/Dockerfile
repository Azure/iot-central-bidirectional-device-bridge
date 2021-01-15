FROM node:12

CMD ["node", "dist/index.js"]

WORKDIR /app
COPY package.json ./
RUN npm i --quiet && npm cache clean --force
COPY ./ ./
RUN npm run build