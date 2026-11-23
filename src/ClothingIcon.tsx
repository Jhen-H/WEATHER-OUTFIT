const imageByClothingId: Record<string, string> = {
  shirt: "shirt-v1.webp", tshirt: "tshirt-v1.webp", "crop-top": "crop-top-v1.webp", "loose-top": "loose-top-v1.webp", "long-sleeve": "long-sleeve-v1.webp", "sleeveless-top": "sleeveless-top-v1.webp", sweatshirt: "sweatshirt-v1.webp",
  "thin-jacket": "thin-jacket-v1.webp", "knit-cardigan": "cardigan-v1.webp", blazer: "blazer-v1.webp", "denim-jacket": "denim-jacket-v1.webp", hoodie: "hoodie-v1.webp", puffer: "puffer-v1.webp", "long-coat": "long-coat-v1.webp",
  "denim-shorts": "denim-shorts-v1.webp", shorts: "shorts-v1.webp", "five-shorts": "five-shorts-v1.webp", "denim-pants": "jeans-v1.webp", "long-pants": "long-pants-v1.webp", "pleated-skirt": "pleated-skirt-v2.webp", "long-skirt": "long-skirt-v1.webp", "wide-pants": "wide-pants-v1.webp",
  dress: "dress-v1.webp", overalls: "overalls-v1.webp", jumpsuit: "jumpsuit-v1.webp",
};

export function ClothingIcon({ id }: { id: string }) {
  const filename = imageByClothingId[id] ?? "tshirt-v1.webp";
  return <img src={`${import.meta.env.BASE_URL}preference-clothing/${filename}`} alt="" aria-hidden="true" />;
}
