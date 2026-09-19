# AutoPartsERP

Governance-first, Arabic-first ERP for an automotive spare-parts business. .NET 9 Clean Architecture +
PostgreSQL 16 + React 19, with ERPNext as a headless accounting engine.

## Start here
- `AGENT_ONBOARDING.md` — read first (context, rules, current state, environment quirks)
- `PROJECT_VISION.md` — stack, screen inventory, debt register, roadmap
- `ENGINEERING_PLAYBOOK.md` — engineering rules and agent roles
- `SETUP_HARDENING.md` — local setup, VPS deployment, troubleshooting, hardening checklist
- `docs/FEATURE_GAP_AND_ROADMAP.md` — gap analysis and phased plan

## Layout
- `src/AutoPartsERP.{Domain,Contracts,Application,Infrastructure,Api}`
- `tests/AutoPartsERP.{UnitTests,IntegrationTests,E2ETests}`
- `frontend` (React SPA), `nginx`, `scripts`, `observability`, `docs`
