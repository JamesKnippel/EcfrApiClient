# ECFR API Client

A modern web application that provides a user-friendly interface to access and navigate the Electronic Code of Federal Regulations (ECFR) data.

## Features

- Browse federal agencies and their regulations with total word counts trends over time
- View agency information and associated regulations titles
- RESTful API backend with .NET 8
- Containerized deployment to Azure Container Apps

## Technology Stack

### Frontend
- **Framework**: Angular 17
- **Language**: TypeScript
- **UI Components**: Angular Material
- **Container**: Docker

### Backend
- **Framework**: .NET 8 Web API
- **Language**: C#
- **Container**: Docker

### Infrastructure
- **Cloud Platform**: Azure Container Apps
- **Container Registry**: Azure Container Registry
- **CI/CD**: GitHub Actions

## Architecture

The application follows a microservices architecture with two main components:

1. **Frontend Service** (`ecfr-client`)
   - Angular SPA hosted in Azure Web Apps
   - Proxies API requests to the backend service
   - Handles routing and user interface

2. **Backend Service** (`EcfrApi.Web`)
   - .NET 8 Web API
   - Provides RESTful endpoints for ECFR data
   - Handles business logic and data access

## Development Setup

1. **Prerequisites**
   - Node.js 18.x
   - .NET 8 SDK
   - Docker
   - Azure CLI (for deployment)

2. **Frontend Setup**
   ```bash
   cd ecfr-client
   npm install
   npm start
   ```

3. **Backend Setup**
   ```bash
   cd EcfrApi.Web
   dotnet restore
   dotnet run
   ```

## Deployment

The application is automatically deployed to Azure Container Apps using GitHub Actions. The workflow:

1. Detects changes in frontend or backend code
2. Builds and tests the affected components
3. Creates Docker images
4. Pushes images to Azure Container Registry
5. Deploys to Azure Container Apps

## Environment Variables

### Frontend
- `API_HOST`: The hostname of the API service

### Backend
- `ASPNETCORE_ENVIRONMENT`: Runtime environment (Development/Production)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## License

[MIT License](LICENSE)
