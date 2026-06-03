.PHONY: quickstart quickstart-source quickstart-down quickstart-logs

quickstart:
	./scripts/quickstart.sh published

quickstart-source:
	./scripts/quickstart.sh source

quickstart-down:
	./scripts/quickstart.sh down

quickstart-logs:
	./scripts/quickstart.sh logs
