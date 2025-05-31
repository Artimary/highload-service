@Library('COSM-Jenkins-libs') _

pipeline {

    agent none

    options {
        // This is required if you want to clean before build
        skipDefaultCheckout(true)
    }

    stages {
        
        stage('Preparation') {
            agent { node { label 'master' } }
            steps {
                step([$class: 'WsCleanup'])
    
                checkout scm

                sh '''#!/bin/bash
                    git log -n 1 | grep "commit " | sed 's/commit //g' > currenntVersion
                '''
                    
                stash name:'workspace', includes:'**'
            }
        }

        stage('Build application') {
            agent { 
                docker {
		    // Put here an image to be used to build the
		    // application
                    image 'maven:3.9.4-eclipse-temurin-17-alpine'
                    // Run the container on the node specified at the
                    // top-level of the Pipeline, in the same workspace,
                    // rather than on a new node entirely:
                    reuseNode true
                    args '-u root'
                }
            }
            steps {
		// Extracting workspace which we created
		// after checkout
                unstash 'workspace'
		// Put build script for your application here
                sh '''
                    #!/bin/bash
                    echo "We can run here something, i.e. flake?"
                '''
            }
        }
        
        stage('Deploy artifacts') {
            agent { 
                docker {
		    // This image contains docker client and 
		    // docker compose utility, so you can create a container
		    // with an up built on previous stage
                    image 'docker-builder'
                    // Run the container on the node specified at the
                    // top-level of the Pipeline, in the same workspace,
                    // rather than on a new node entirely:
                    reuseNode true
                    args '-u root --net="main_bridge" -v /var/run/docker.sock:/var/run/docker.sock'
                } 
            }
            steps {

                sh '''
                    #!/bin/bash
                    set -e

                    GIT_REVISION=`cat currenntVersion`
                    # docker build / run ....
		    # docker-compose ...
                '''
            }
         }
    }

    post {
        always {
            node ('master') {
                script {
                    env.GIT_URL = env.GIT_URL_1
		    notifyRocketChat(
                        channelName: 'dummy',
                        minioCredentialsId: 'jenkins-minio-credentials',
                        minioHostUrl: 'https://minio.cloud.cosm-lab.science'
                    )
                    withCredentials([string(credentialsId: 'CloudRushTlg-token', variable: 'TLG_TOKEN')]) {
                        notifyTelegram(
                            minioHostUrl: 'https://minio.cloud.cosm-lab.science',
                            botIdAndToken: env.TLG_TOKEN,
                            chatId: '-1002474884172',
                            threadId: '2'
                        )
                    }
                }
            }
        }
    }
 }
