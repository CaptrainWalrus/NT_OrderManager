# Use an official Node.js runtime as a parent image
FROM node:18-alpine

# Set the working directory in the container
WORKDIR /usr/src/app

# Install system dependencies required for node-canvas (used by chartjs-node-canvas)
# This is crucial for chart generation to work on a minimal base image like Alpine
RUN apk add --no-cache build-base g++ cairo-dev jpeg-dev pango-dev giflib-dev

# Copy package.json and package-lock.json to leverage Docker cache
COPY package*.json ./

# Install application dependencies
RUN npm install --production

# Bundle app source
COPY . .

# Expose the port the app runs on
EXPOSE 8000

# Define the command to run your app
CMD [ "node", "server.js" ] 