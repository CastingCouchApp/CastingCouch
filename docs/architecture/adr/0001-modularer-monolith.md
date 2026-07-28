# ADR 0001: Modularer Monolith bleibt Zielarchitektur

- Status: Angenommen
- Datum: 28. Juli 2026

## Entscheidung

Creator Control Suite bleibt eine WPF/.NET-10-Anwendung als modularer Monolith.
Core enthält Contracts und reine Policies. Integrationsmodule enthalten jeweils
einen externen Partner. WPF- und modulübergreifende Use Cases liegen in
testbaren App-Services.

## Konsequenzen

Es gibt keinen Microservice- oder UI-Framework-Wechsel. Projekt- und
Namespace-Grenzen werden durch Architekturtests geschützt. Die Shell wird
inkrementell statt per Big Bang zerlegt.
