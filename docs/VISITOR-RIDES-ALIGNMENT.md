# Visitor rides-column alignment

The All Visitors list now uses left alignment for its rides count. Number and time
remain centred; cash remains right-aligned. Headers, values, sorting, refresh and
selection are unchanged.

The previous UI-907 explanation treated the alignment store as a different
constructor. It is part of the rides-cell constructor. The row setup leaves that
default in place while explicitly overriding the other text columns. This resolves
the source uncertainty; it does not establish a rendered pass.

`VisitorRidesAlignmentTests` draws the real visitor list into the existing CPU
`UiBatch`, with synthetic digits and no original media. It checks the actual rides
value and position, the other three text columns, two text scales and the existing
RTL alignment mirroring. These assertions are authored and source-reviewed only
until CI executes them. UI-907 remains registered pending execution; native pixels,
font choice and the other visitor timing/membership gates are separate.
