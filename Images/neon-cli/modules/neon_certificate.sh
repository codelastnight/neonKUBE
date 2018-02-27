#!/bin/bash
#
# This script invokes the [neon-cli] command that 
# implements this module.

# This marker tells Ansible that we want JSON formatted arguments.
WANT_JSON=yes

# Invoke the module as a [neon-cli] command.
neon ansible module neon_certificate $@
