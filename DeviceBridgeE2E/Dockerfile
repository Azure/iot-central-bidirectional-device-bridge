# node:12.18.2-slim [linux; amd64] (https://github.com/docker-library/repo-info/blob/master/repos/node/remote/12.18.2-slim.md) 
FROM node@sha256:99630ba20d3a68298ef4f0d826a29acd364ef8442b7e1edbcc75c9f6ddcf2a5a

# Install prerequisites for azure cli/kubectl
RUN apt-get update && apt-get install -y ca-certificates curl apt-transport-https lsb-release gnupg2

# Install azure cli
RUN curl -sL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor | tee /etc/apt/trusted.gpg.d/microsoft.gpg > /dev/null && \
    echo "deb [arch=amd64] https://packages.microsoft.com/repos/azure-cli/ $(lsb_release -cs) main" | tee /etc/apt/sources.list.d/azure-cli.list && \
    apt-get update && \
    apt-get install -y azure-cli

# Install kubectl
RUN curl -s https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key add - && \
    echo "deb https://apt.kubernetes.io/ kubernetes-xenial main" | tee -a /etc/apt/sources.list.d/kubernetes.list && \
    apt-get update && \
    apt-get install -y kubectl

# Set the default executable for the image:
CMD [ "npm", "start" ]

# Create app directories:
WORKDIR /app

# Copy required files to install packages including the client:
COPY package.json package-lock.json .npmrc ./

# Install packages:
RUN npm ci --quiet && npm cache clean --force

# Set production mode:
ENV NODE_ENV production

# Copy all files in src directory to image:
COPY ./ ./

# Remove secret file and run all commands:
RUN rm -f .npmrc && npm run build:verify
