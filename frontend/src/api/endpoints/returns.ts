/** Shapes shared by sales and purchase returns (the server's Contracts.Common.ReturnDtos). */

/** A line of the original with what posted returns took back and what may still be returned, at the original price / cost. */
export type ReturnableLine = {
  lineId: string; lineNumber: number; code: string; name: string; quantity: number; returned: number; returnable: number;
  unitPriceUsd: number; discountPct: number; locationId: string | null;
};

export type ReturnLineInput = { lineId: string; quantity: number; locationId?: string };

export type CreateReturn = { returnDate: string; reason?: string; lines: ReturnLineInput[] };

/** A linked document: a return and the invoice it returns. */
export type DocumentLink = { id: string; number: string; status: string; date: string; totalUsd: number };
