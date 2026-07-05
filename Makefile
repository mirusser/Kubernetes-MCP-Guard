TAG ?= latest

.PHONY: quickstart quickstart-source quickstart-down quickstart-logs dashboard-url

quickstart:
	./scripts/quickstart.sh published --tag $(TAG)

quickstart-source:
	./scripts/quickstart.sh source

quickstart-down:
	./scripts/quickstart.sh down

quickstart-logs:
	./scripts/quickstart.sh logs

dashboard-url:
	./scripts/print-dashboard-url.sh
