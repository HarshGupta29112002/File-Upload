pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        SOLUTION_NAME               = 'FileUploadService.sln'
        API_PROJECT                 = 'FileUploadService/FileUploadService.csproj'
        TEST_PROJECT                = 'FileUploadService.XunitTesting/FileUploadService.XunitTesting.csproj'
        DOCKER_IMAGE_NAME           = 'file-upload-service'
        DOCKER_REGISTRY             = 'your-registry.example.com'
        COVERAGE_THRESHOLD          = '70'
    }

    stages {

        stage('Checkout') {
            steps {
                checkout scm
                echo "✅ Source checked out"
            }
        }

        stage('Restore') {
            steps {
                sh "dotnet restore ${SOLUTION_NAME}"
                echo "✅ NuGet packages restored"
            }
        }

        stage('Build') {
            steps {
                sh "dotnet build ${SOLUTION_NAME} -c Release --no-restore"
                echo "✅ Solution built"
            }
        }

        stage('Unit Tests') {
            steps {
                sh """
                    dotnet test ${TEST_PROJECT} \
                        --no-build \
                        --configuration Release \
                        --filter 'FullyQualifiedName~UnitTests' \
                        --collect:'XPlat Code Coverage' \
                        --results-directory ./TestResults/UnitTests \
                        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
                """
            }
            post {
                always {
                    publishTestResults testResultsFiles: '**/TestResults/**/*.xml', skipPublishingChecks: true
                }
            }
        }

        stage('Integration Tests') {
            steps {
                sh """
                    dotnet test ${TEST_PROJECT} \
                        --no-build \
                        --configuration Release \
                        --filter 'FullyQualifiedName~IntegrationTests' \
                        --results-directory ./TestResults/IntegrationTests
                """
            }
        }

        stage('BDD Tests') {
            steps {
                sh """
                    dotnet test ${TEST_PROJECT} \
                        --no-build \
                        --configuration Release \
                        --filter 'FullyQualifiedName~BDD' \
                        --results-directory ./TestResults/BDD
                """
            }
        }

        stage('Coverage Report') {
            steps {
                sh """
                    dotnet tool install -g dotnet-reportgenerator-globaltool || true
                    reportgenerator \
                        -reports:'./TestResults/**/*.xml' \
                        -targetdir:'./coveragereport' \
                        -reporttypes:'Html;Cobertura'
                """
            }
            post {
                always {
                    publishHTML(target: [
                        allowMissing: true,
                        alwaysLinkToLastBuild: true,
                        keepAll: true,
                        reportDir: 'coveragereport',
                        reportFiles: 'index.html',
                        reportName: 'Code Coverage Report'
                    ])
                }
            }
        }

        stage('Publish') {
            when { branch 'main' }
            steps {
                sh "dotnet publish ${API_PROJECT} -c Release -o ./publish"
                echo "✅ Published"
            }
        }

        stage('Docker Build & Push') {
            when { branch 'main' }
            steps {
                sh """
                    docker build -t ${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME}:${BUILD_NUMBER} .
                    docker tag ${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME}:${BUILD_NUMBER} \
                                ${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME}:latest
                    docker push ${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME}:${BUILD_NUMBER}
                    docker push ${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME}:latest
                """
            }
        }

        stage('Deploy to Kubernetes') {
            when { branch 'main' }
            steps {
                sh """
                    helm upgrade --install file-upload-service \
                        ./FileUploadService/helm \
                        --set image.tag=${BUILD_NUMBER} \
                        --set image.repository=${DOCKER_REGISTRY}/${DOCKER_IMAGE_NAME} \
                        --namespace file-upload \
                        --create-namespace
                """
            }
        }
    }

    post {
        always {
            cleanWs()
        }
        success {
            echo "🎉 Pipeline passed — FileUploadService build successful!"
        }
        failure {
            echo "❌ Pipeline failed — check the logs above."
        }
    }
}
