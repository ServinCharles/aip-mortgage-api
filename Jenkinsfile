// Build pipeline for aip-api (.NET 8)
//
// Build once, test, scan, then publish ONE immutable image tagged with the git commit.
// The deploy pipeline later promotes that exact image through sit-01 -> sit-02.
//
//   Checkout -> Build & Unit Test -> SonarQube -> Snyk -> Docker Build -> Image Scan -> Push to ECR
//
// Every tool runs in a Docker container, so the Jenkins machine only needs Docker.
// SonarQube, Snyk and ECR push are switched off by default until their credentials are set up.

pipeline {
    agent any

    options {
        timeout(time: 30, unit: 'MINUTES')
        buildDiscarder(logRotator(numToKeepStr: '20'))
        disableConcurrentBuilds()
    }

    parameters {
        booleanParam(name: 'RUN_SONAR', defaultValue: false, description: 'SonarQube analysis + quality gate (needs the sonar-token credential)')
        string(name: 'SONAR_HOST_URL', defaultValue: 'http://host.docker.internal:9000', description: 'SonarQube URL, or https://sonarcloud.io')
        string(name: 'SONAR_ORGANIZATION', defaultValue: '', description: 'SonarCloud organisation (leave blank for SonarQube)')
        booleanParam(name: 'RUN_SNYK', defaultValue: false, description: 'Snyk dependency scan (needs the snyk-token credential)')
        choice(name: 'IMAGE_SCANNER', choices: ['trivy', 'twistlock'], description: 'Container image scanner. twistlock needs twistcli on PATH and the prisma-cloud credential')
        string(name: 'PRISMA_CONSOLE_URL', defaultValue: '', description: 'Prisma Cloud (Twistlock) console URL, only for IMAGE_SCANNER=twistlock')
        string(name: 'PLATFORM', defaultValue: 'linux/amd64', description: 'Image platform: linux/amd64 for EKS, linux/arm64 for local Kubernetes on Apple Silicon')
        booleanParam(name: 'PUSH_TO_ECR', defaultValue: false, description: 'Push the scanned image to Amazon ECR (needs the aws-credentials credential)')
        string(name: 'AWS_REGION', defaultValue: 'eu-west-2', description: 'AWS region of the ECR repository')
    }

    environment {
        PATH = "/Applications/Docker.app/Contents/Resources/bin:/opt/homebrew/bin:/usr/local/bin:${PATH}"
        SERVICE = 'aip-api'
        SONAR_PROJECT_KEY = 'aip-mortgage-api'
        DOTNET_SDK_IMAGE = 'mcr.microsoft.com/dotnet/sdk:8.0'
        TRIVY_IMAGE = 'aquasec/trivy:0.58.1'
        SNYK_IMAGE = 'snyk/snyk:dotnet'
        AWS_CLI_IMAGE = 'amazon/aws-cli:2.22.0'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    env.IMAGE_TAG = sh(script: 'git rev-parse --short HEAD', returnStdout: true).trim()
                    env.IMAGE = "${env.SERVICE}:${env.IMAGE_TAG}"
                    currentBuild.displayName = "#${env.BUILD_NUMBER} ${env.IMAGE_TAG}"
                }
                echo "Building ${env.IMAGE}"
            }
        }

        stage('Build & Unit Test') {
            steps {
                // Runs as the Jenkins user so the workspace doesn't fill up with root-owned files
                sh '''
                    rm -rf TestResults
                    docker run --rm --user "$(id -u):$(id -g)" \
                      -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
                      -v "$WORKSPACE":/src -w /src "$DOTNET_SDK_IMAGE" \
                      dotnet test tests/AipApi.Tests -c Release \
                        --logger "trx;LogFileName=unit-tests.trx" \
                        --logger "junit;LogFilePath=/src/TestResults/junit/unit-tests.xml" \
                        --collect:"XPlat Code Coverage;Format=cobertura,opencover" \
                        --results-directory /src/TestResults
                '''
            }
            post {
                always {
                    junit testResults: 'TestResults/junit/*.xml', allowEmptyResults: true
                    archiveArtifacts artifacts: 'TestResults/**/*.xml, TestResults/**/*.trx', allowEmptyArchive: true
                }
            }
        }

        stage('SonarQube') {
            when { expression { params.RUN_SONAR } }
            steps {
                withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
                    // The .NET scanner needs Java, so this one container runs as root and hands
                    // the workspace back to the Jenkins user when it finishes.
                    sh '''
                        docker run --rm -e SONAR_TOKEN -e SONAR_HOST_URL -e SONAR_ORGANIZATION -e SONAR_PROJECT_KEY \
                          -e OWNER="$(id -u):$(id -g)" \
                          -v "$WORKSPACE":/src -w /src "$DOTNET_SDK_IMAGE" bash -ceu '
                            trap "chown -R $OWNER /src" EXIT
                            apt-get update -qq && apt-get install -y -qq openjdk-17-jre-headless > /dev/null
                            dotnet tool install --global dotnet-sonarscanner > /dev/null
                            export PATH="$PATH:/root/.dotnet/tools"
                            ORG_ARG=""
                            if [ -n "$SONAR_ORGANIZATION" ]; then ORG_ARG="/o:$SONAR_ORGANIZATION"; fi
                            dotnet sonarscanner begin /k:"$SONAR_PROJECT_KEY" $ORG_ARG \
                              /d:sonar.host.url="$SONAR_HOST_URL" /d:sonar.token="$SONAR_TOKEN" \
                              /d:sonar.cs.opencover.reportsPaths="TestResults/**/coverage.opencover.xml" \
                              /d:sonar.exclusions="tests/**" \
                              /d:sonar.qualitygate.wait=true
                            dotnet build src/AipApi -c Release
                            dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
                          '
                    '''
                }
            }
        }

        stage('Snyk') {
            when { expression { params.RUN_SNYK } }
            steps {
                withCredentials([string(credentialsId: 'snyk-token', variable: 'SNYK_TOKEN')]) {
                    sh '''
                        docker run --rm -e SNYK_TOKEN -v "$WORKSPACE":/project -w /project "$SNYK_IMAGE" \
                          snyk test --file=src/AipApi/AipApi.csproj --severity-threshold=high
                    '''
                }
            }
        }

        stage('Docker Build') {
            steps {
                sh '''
                    docker build --platform "$PLATFORM" \
                      --label "org.opencontainers.image.revision=$GIT_COMMIT" \
                      --label "org.opencontainers.image.source=$GIT_URL" \
                      -t "$IMAGE" .
                '''
            }
        }

        stage('Image Scan') {
            steps {
                script {
                    if (params.IMAGE_SCANNER == 'twistlock') {
                        withCredentials([usernamePassword(credentialsId: 'prisma-cloud', usernameVariable: 'PC_USER', passwordVariable: 'PC_PASS')]) {
                            sh 'twistcli images scan --address "$PRISMA_CONSOLE_URL" -u "$PC_USER" -p "$PC_PASS" --details "$IMAGE"'
                        }
                    } else {
                        trivyScan()
                    }
                }
            }
            post {
                always {
                    archiveArtifacts artifacts: 'reports/trivy-*.json', allowEmptyArchive: true
                }
            }
        }

        stage('Push to ECR') {
            when { expression { params.PUSH_TO_ECR } }
            steps {
                withCredentials([usernamePassword(credentialsId: 'aws-credentials', usernameVariable: 'AWS_ACCESS_KEY_ID', passwordVariable: 'AWS_SECRET_ACCESS_KEY')]) {
                    pushToEcr()
                }
            }
        }
    }

    post {
        always {
            sh 'echo "IMAGE=${IMAGE}" > image.properties'
            archiveArtifacts artifacts: 'image.properties', allowEmptyArchive: true
        }
        success {
            echo "✅ ${env.IMAGE} built, tested and scanned"
        }
        failure {
            echo '❌ Build pipeline failed, check the failing stage above'
        }
        cleanup {
            deleteDir()
        }
    }
}

// Writes a full JSON report, then fails the build on fixable HIGH/CRITICAL vulnerabilities
def trivyScan() {
    sh '''
        mkdir -p reports
        TRIVY="docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v trivy-cache:/root/.cache/ -v $WORKSPACE/reports:/reports $TRIVY_IMAGE"
        $TRIVY image --no-progress --format json --output "/reports/trivy-$SERVICE.json" "$IMAGE"
        $TRIVY image --no-progress --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed "$IMAGE"
    '''
}

// Tags the scanned image for ECR, pushes it, and records the full image reference for the deploy pipeline
def pushToEcr() {
    sh '''
        AWS="docker run --rm -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e AWS_DEFAULT_REGION=$AWS_REGION $AWS_CLI_IMAGE"
        ACCOUNT="$($AWS sts get-caller-identity --query Account --output text)"
        REGISTRY="$ACCOUNT.dkr.ecr.$AWS_REGION.amazonaws.com"
        $AWS ecr get-login-password | docker login --username AWS --password-stdin "$REGISTRY"
        docker tag "$IMAGE" "$REGISTRY/$IMAGE"
        docker push "$REGISTRY/$IMAGE"
        echo "IMAGE=$REGISTRY/$IMAGE" > ecr-image.properties
    '''
    archiveArtifacts artifacts: 'ecr-image.properties'
}
