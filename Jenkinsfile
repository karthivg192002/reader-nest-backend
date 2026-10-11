// Deploys the backend API container. Every secret below (DB password, JWT signing key,
// S3 access/secret key) comes from Jenkins' Credentials store via withCredentials() — never
// hardcoded here. See the "Jenkins setup required" note at the bottom of this file for the
// exact credential IDs this pipeline expects; create them once in
// Manage Jenkins -> Credentials before the first run.
pipeline {
    agent any

    environment {
        IMAGE_NAME         = 'deepakrajv/readernestackend'
        CONTAINER_NAME     = 'readernestbackend'
        GITHUB_REPO        = 'https://github.com/karthivg192002/reader-nest-backend.git'

        DB_HOST            = '204.168.140.222'
        DB_PORT            = '5432'
        DB_NAME            = 'reader_nest'
        DB_USER            = 'admin'

        // Not secret on their own (an endpoint hostname and a bucket name reveal nothing
        // exploitable without the access/secret key pair, which ARE credential-bound below).
        STORAGE_S3_ENDPOINT      = 'hel1.your-objectstorage.com'
        STORAGE_S3_BUCKET_NAME   = 'readernest'

        // Where uploaded resources and class presentations live: 'S3' (the bucket above) or
        // 'Local' (STORAGE_LOCAL_HOST_PATH on this server, mounted into the container). Before
        // switching, copy the existing files across -- see StorageMigration.cs:
        //   docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to local
        STORAGE_PROVIDER         = 'S3'
        STORAGE_LOCAL_HOST_PATH  = '/opt/readernest/uploads'

        DOCKER_NETWORK     = 'reader-network'
    }

    triggers {
        githubPush()
    }

    stages {

        stage('Checkout') {
            steps {
                git(
                    branch: 'main',
                    url: "${GITHUB_REPO}",
                    credentialsId: 'github'
                )
            }
        }

        stage('Build Docker Image') {
            steps {
                sh """
                    docker build \
                      -t ${IMAGE_NAME}:latest \
                      -f iucs.readernest.api/Dockerfile .
                """
            }
        }

        stage('Create Docker Network') {
            steps {
                sh """
                    docker network inspect ${DOCKER_NETWORK} >/dev/null 2>&1 || \
                    docker network create ${DOCKER_NETWORK}
                """
            }
        }

        stage('Prepare Upload Folder') {
            steps {
                // The host folder local storage writes to. Always mounted (harmless while on S3) so
                // switching STORAGE_PROVIDER never strands files inside a container that a redeploy
                // throws away -- how files were lost before the move to S3. Owned by the container's
                // non-root app user (uid 1654 in the .NET images), set via a throwaway container so no
                // sudo is needed on the host.
                sh """
                    mkdir -p ${STORAGE_LOCAL_HOST_PATH} || true
                    docker run --rm -u 0 -v ${STORAGE_LOCAL_HOST_PATH}:/data alpine sh -c 'chown 1654:1654 /data' || echo 'WARNING: could not set upload folder owner (only matters with STORAGE_PROVIDER=Local)'
                """
            }
        }

        stage('Remove Old Container') {
            steps {
                sh """
                    docker stop ${CONTAINER_NAME} || true
                    docker rm ${CONTAINER_NAME} || true
                """
            }
        }

        stage('Run Container') {
            steps {
                // withCredentials binds each secret to a short-lived env var for just this
                // shell step; Jenkins also masks their values out of the console log
                // automatically. Nothing here is ever written to disk or to the image.
                withCredentials([
                    string(credentialsId: 'readernest-db-password',    variable: 'DB_PASS'),
                    string(credentialsId: 'readernest-jwt-signing-key', variable: 'JWT_SIGNING_KEY'),
                    string(credentialsId: 'readernest-s3-access-key',  variable: 'S3_ACCESS_KEY'),
                    string(credentialsId: 'readernest-s3-secret-key',  variable: 'S3_SECRET_KEY'),
                ]) {
                    sh """
                        docker run -d \
                          --name ${CONTAINER_NAME} \
                          --network ${DOCKER_NETWORK} \
                          -p 1002:8080 \
                          --restart unless-stopped \
                          -e ASPNETCORE_ENVIRONMENT=Production \
                          -e ASPNETCORE_URLS=http://+:8080 \
                          -e ConnectionStrings__ReaderNestDb="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASS}" \
                          -e Jwt__SigningKey="${JWT_SIGNING_KEY}" \
                          -e Storage__S3__Endpoint="${STORAGE_S3_ENDPOINT}" \
                          -e Storage__S3__AccessKey="${S3_ACCESS_KEY}" \
                          -e Storage__S3__SecretKey="${S3_SECRET_KEY}" \
                          -e Storage__S3__BucketName="${STORAGE_S3_BUCKET_NAME}" \
                          -e Storage__Provider="${STORAGE_PROVIDER}" \
                          -e Storage__LocalPath=/app/uploads \
                          -v ${STORAGE_LOCAL_HOST_PATH}:/app/uploads \
                          ${IMAGE_NAME}:latest
                    """
                }
            }
        }

        stage('Verify Deployment') {
            steps {
                sh """
                    sleep 15
                    docker ps | grep ${CONTAINER_NAME}
                    docker logs --tail 50 ${CONTAINER_NAME}
                """
            }
        }
    }

    post {
        success {
            echo '✅ ReaderNest Backend deployed successfully on port 1002'
        }

        failure {
            echo '❌ Deployment failed. Check Jenkins console logs.'
        }

        always {
            cleanWs()
        }
    }
}

// ---------------------------------------------------------------------------------------
// Jenkins setup required (one-time, before the first run of this pipeline):
// Manage Jenkins -> Credentials -> System -> Global credentials -> Add Credentials
// Kind = "Secret text" for each of these four credential IDs. The actual secret values are
// NOT recorded in this file (or anywhere else in this repo) on purpose — get them from
// wherever your team keeps real secrets out-of-band:
//
//   readernest-db-password
//   readernest-jwt-signing-key   (rotating this invalidates every existing session — that
//                                 is the intended effect if the previous deploy was ever
//                                 running with a guessable/placeholder key, not a side
//                                 effect to work around)
//   readernest-s3-access-key
//   readernest-s3-secret-key
// ---------------------------------------------------------------------------------------
