# CodexWingman Domain Language

## Wingman Host

The installed desktop application that discovers, activates, refreshes, and removes Helpers. It owns Windows integration and communication with Codex windows.

## Helper

An independently installable feature that changes or augments Codex behavior. A Helper is distributed as readable files in one folder and is identified by a stable ID.

## Helper Package

The folder containing a Helper's manifest and scripts. A package can be bundled with Wingman or installed by the user without rebuilding the Wingman Host.

## Helper Manifest

The readable declaration of a Helper's identity, version, refresh schedule, entry points, and requested Capabilities.

## Capability

A named Wingman Host ability that a Helper may use, such as reading Codex session files or requesting a new Codex window. Capabilities describe available behavior; they are not a security guarantee.

## Activation

The user's persisted choice that a Helper should be applied and kept current. Deactivation removes that Helper's owned effects.

## Reconciliation

The repeated act of making every current Codex window match all active Helpers, including after a window opens, closes, or rerenders.

## Renderer Script

Readable JavaScript that runs inside a Codex window to apply or remove a Helper's visible behavior.

## Backend Script

Optional readable JavaScript interpreted by the Wingman Host to gather state for a Helper. It runs without UAC, PowerShell, Node, or direct access to arbitrary .NET types.

## Host Action

A request sent from a Renderer Script to the Wingman Host for behavior that cannot be completed inside a Codex window, such as opening a new native Codex window.
