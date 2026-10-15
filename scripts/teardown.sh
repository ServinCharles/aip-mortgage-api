#!/usr/bin/env bash
# Remove everything from AWS so it stops costing money.
# The namespace goes first: its LoadBalancer Service owns an AWS load balancer that Terraform
# doesn't know about, and it would otherwise block the VPC from being deleted.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REGION="${AWS_REGION:-eu-west-2}"
CLUSTER="${CLUSTER_NAME:-aip-mortgage}"

if aws eks describe-cluster --region "$REGION" --name "$CLUSTER" >/dev/null 2>&1; then
  aws eks update-kubeconfig --region "$REGION" --name "$CLUSTER" >/dev/null
  echo "==> Deleting namespace aip-mortgage (and its load balancer)"
  kubectl delete namespace aip-mortgage --ignore-not-found --wait=true
fi

echo "==> Destroying cluster, VPC and ECR repositories"
terraform -chdir="${ROOT}/infra/terraform" destroy -auto-approve \
  -var "region=${REGION}" -var "cluster_name=${CLUSTER}"
